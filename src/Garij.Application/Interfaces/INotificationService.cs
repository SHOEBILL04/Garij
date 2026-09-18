using Garij.Application.DTOs;
using Garij.Domain.Enums;

namespace Garij.Application.Interfaces;

public interface INotificationService
{
    Task<IEnumerable<NotificationDto>> GetAllNotificationsAsync();

    Task<NotificationDto?> GetNotificationByIdAsync(int id);

    Task<IEnumerable<NotificationDto>> GetNotificationsByServiceJobAsync(int serviceJobId);

    Task<IEnumerable<NotificationDto>> GetPendingNotificationsAsync();

    Task<NotificationDto> CreateNotificationAsync(NotificationDto notification);

    /// <summary>
    /// Records the decision on the notification row only; it never touches the job. To respond as
    /// a user and have the decision act on the job, use IServiceJobService.RespondToNotificationAsync.
    /// </summary>
    Task<NotificationDto> RespondToNotificationAsync(int notificationId, NotificationStatus status);
}
