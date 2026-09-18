using Garij.Domain.Enums;

namespace Garij.Application.DTOs;

public class NotificationDto
{
    public int Id { get; set; }

    public int ServiceJobId { get; set; }

    public string Message { get; set; } = string.Empty;

    public NotificationType Type { get; set; } = NotificationType.JobCompleted;

    public NotificationStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public string? GarageId { get; set; }
}
