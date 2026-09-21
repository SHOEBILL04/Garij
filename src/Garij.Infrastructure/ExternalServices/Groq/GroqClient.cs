using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Garij.Infrastructure.ExternalServices.Gemini;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Garij.Infrastructure.ExternalServices.Groq;

public class GroqClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly GroqSettings _settings;
    private readonly ILogger<GroqClient> _logger;

    public GroqClient(
        HttpClient httpClient,
        IOptions<GroqSettings> settings,
        ILogger<GroqClient> logger)
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
        var apiKey = !string.IsNullOrWhiteSpace(_settings.ApiKey)
            ? _settings.ApiKey
            : Environment.GetEnvironmentVariable("GROQ_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Groq API Key is not configured in application settings, user secrets, or environment variables.");
        }

        var baseUrl = _settings.BaseUrl.TrimEnd('/') + "/";
        var requestUri = $"{baseUrl}chat/completions";

        var enrichedSystemPrompt = $"{systemPrompt}\n\nCRITICAL INSTRUCTION: You MUST return a single valid JSON object adhering strictly to the requested schema. Do not enclose in markdown code fences or backticks. Output valid JSON only.";

        var payload = new
        {
            model = !string.IsNullOrWhiteSpace(_settings.Model) ? _settings.Model : "openai/gpt-oss-120b",
            messages = new object[]
            {
                new { role = "system", content = enrichedSystemPrompt },
                new { role = "user", content = userPrompt }
            },
            response_format = new { type = "json_object" },
            temperature = 0.2
        };

        var jsonBody = JsonSerializer.Serialize(payload);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Groq Prompt Sent: Model={Model} | System={SystemPrompt} | User={UserPrompt}", _settings.Model, systemPrompt, userPrompt);
        }

        const int maxRetries = 3;
        var retryDelays = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (attempt < maxRetries && ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Groq request attempt {Attempt} encountered network/timeout error. Retrying...", attempt + 1);
                await Task.Delay(retryDelays[attempt], cancellationToken);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                _logger.LogWarning("Groq rate limit (429) hit on attempt {Attempt}. Backing off for {Delay}s.", attempt + 1, retryDelays[attempt].TotalSeconds);
                await Task.Delay(retryDelays[attempt], cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Groq API returned HTTP {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
                throw new HttpRequestException($"Groq API error ({response.StatusCode}): {errorBody}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Groq Raw Response: {ResponseBody}", responseBody);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var contentEl))
            {
                var text = contentEl.GetString() ?? string.Empty;

                // Strip any markdown code fences if LLM wrapped it
                var cleaned = Regex.Replace(text, @"^```(?:json)?\s*|\s*```$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase).Trim();
                return cleaned;
            }

            throw new InvalidOperationException("Groq response did not contain valid text content in choices.");
        }

        throw new HttpRequestException("Groq API call failed after max retries.");
    }
}
