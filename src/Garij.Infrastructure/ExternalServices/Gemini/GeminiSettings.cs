namespace Garij.Infrastructure.ExternalServices.Gemini;

/// <summary>
/// Bound from the "GeminiSettings" section of appsettings.json via the options pattern.
/// </summary>
public class GeminiSettings
{
    public const string SectionName = "GeminiSettings";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-3.6-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
}
