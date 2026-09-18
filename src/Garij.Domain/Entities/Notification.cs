using Garij.Domain.Enums;

namespace Garij.Domain.Entities;

/// <summary>Customer-facing notification (e.g. approval request) tied to a ServiceJob.</summary>
public class Notification
{
    public int Id { get; set; }

    public int ServiceJobId { get; set; }

    public ServiceJob ServiceJob { get; set; } = null!;

    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// What the notification is for, which decides whether responding to it acts on the job.
    /// Recorded explicitly rather than inferred from Message, which is free-form text.
    /// </summary>
    public NotificationType Type { get; set; } = NotificationType.JobCompleted;

    public NotificationStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public string? GarageId { get; set; }
}
