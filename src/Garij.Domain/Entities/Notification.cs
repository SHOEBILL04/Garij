using Garij.Domain.Enums;

namespace Garij.Domain.Entities;

/// <summary>Customer-facing notification (e.g. approval request) tied to a ServiceJob.</summary>
public class Notification
{
    public int Id { get; set; }

    public int ServiceJobId { get; set; }

    public ServiceJob ServiceJob { get; set; } = null!;

    public string Message { get; set; } = string.Empty;

    public NotificationStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public string? GarageId { get; set; }
}
