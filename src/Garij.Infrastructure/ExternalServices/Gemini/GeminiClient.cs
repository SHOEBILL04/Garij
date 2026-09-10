using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Garij.Infrastructure.ExternalServices.Gemini;

public class GeminiClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly GeminiSettings _settings;
    private readonly ILogger<GeminiClient> _logger;

    public GeminiClient(
        HttpClient httpClient,
        IOptions<GeminiSettings> settings,
        ILogger<GeminiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> GenerateJsonContentAsync(
        string systemPrompt,
        string userPrompt,
        string jsonResponseSchema,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException("Gemini API Key is not configured in application settings or user secrets.");
        }

        var baseUrl = _settings.BaseUrl.TrimEnd('/') + "/";
        var requestUri = $"{baseUrl}models/{_settings.Model}:generateContent";

        // Build server-side JSON schema element
        using var schemaDoc = JsonDocument.Parse(jsonResponseSchema);

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = schemaDoc.RootElement.Clone()
            }
        };

        var jsonBody = JsonSerializer.Serialize(payload);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Gemini Prompt Sent: System={SystemPrompt} | User={UserPrompt}", systemPrompt, userPrompt);
        }

        const int maxRetries = 3;
        var retryDelays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };

            // Mandate: Send API key via header to avoid URI logging leakage
            request.Headers.Add("x-goog-api-key", _settings.ApiKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxRetries && ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Gemini request attempt {Attempt} encountered network/timeout error. Retrying...", attempt + 1);
                await Task.Delay(retryDelays[attempt], cancellationToken);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                _logger.LogWarning("Gemini rate limit (429) hit on attempt {Attempt}. Backing off for {Delay}s.", attempt + 1, retryDelays[attempt].TotalSeconds);
                await Task.Delay(retryDelays[attempt], cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Gemini API returned HTTP {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
                throw new HttpRequestException($"Gemini API error ({response.StatusCode}): {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Gemini Raw Response: {ResponseBody}", responseBody);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var textEl))
            {
                return textEl.GetString() ?? string.Empty;
            }

            throw new InvalidOperationException("Gemini response did not contain valid text content in candidates.");
        }

        throw new HttpRequestException("Gemini API call failed after max retries.");
    }
}
