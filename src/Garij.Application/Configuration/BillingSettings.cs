namespace Garij.Application.Configuration;

/// <summary>Bound from the "BillingSettings" section of appsettings.json via the options pattern.</summary>
public class BillingSettings
{
    public const string SectionName = "BillingSettings";

    public decimal TaxRatePercent { get; set; }
}
