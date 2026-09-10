namespace Garij.Infrastructure.ExternalServices.Gemini;

/// <summary>
/// Abstraction for invoking a Large Language Model with structured server-side JSON constraints.
/// </summary>
public interface ILlmClient
{
    Task<string> GenerateJsonContentAsync(
        string systemPrompt,
        string userPrompt,
        string jsonResponseSchema,
        CancellationToken cancellationToken = default);
}
