namespace Garij.Application.Configuration;

/// <summary>
/// Bound from the "LicenseSettings" section of appsettings.json via the options pattern.
/// </summary>
public class LicenseSettings
{
    public const string SectionName = "LicenseSettings";

    public decimal Price { get; set; } = 499.00m;

    public string Currency { get; set; } = "USD";

    public string ProductName { get; set; } = "Garij Intelligent Vehicle Workshop — Lifetime License";
}
