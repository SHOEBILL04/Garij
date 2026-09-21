namespace Garij.Infrastructure.ExternalServices.Groq;

/// <summary>
/// Bound from the "GroqSettings" section of appsettings.json or environment variables via the options pattern.
/// </summary>
public class GroqSettings
{
    public const string SectionName = "GroqSettings";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "openai/gpt-oss-120b";

    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1/";
}
