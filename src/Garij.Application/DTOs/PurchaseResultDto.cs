namespace Garij.Application.DTOs;

public class PurchaseResultDto
{
    public bool Success { get; set; }

    public string? Message { get; set; }

    public ProjectPurchaseDto? Purchase { get; set; }

    public string? LicenseKey { get; set; }

    public bool AccountCreated { get; set; }

    public static PurchaseResultDto Successful(ProjectPurchaseDto purchase, string? message = null, bool accountCreated = false) =>
        new()
        {
            Success = true,
            Purchase = purchase,
            LicenseKey = purchase.LicenseKey,
            Message = message ?? "Payment processed and lifetime license activated successfully.",
            AccountCreated = accountCreated
        };

    public static PurchaseResultDto Failed(string errorMessage) =>
        new()
        {
            Success = false,
            Message = errorMessage
        };
}
