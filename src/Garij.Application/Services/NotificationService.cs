using Garij.Application.DTOs;
using Garij.Application.Interfaces;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Repositories;

namespace Garij.Application.Services;

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notificationRepository;

    public NotificationService(INotificationRepository notificationRepository)
    {
        _notificationRepository = notificationRepository;
    }

    public async Task<IEnumerable<NotificationDto>> GetAllNotificationsAsync()
    {
        var notifications = await _notificationRepository.GetAllAsync();
        return notifications.Select(ToDto);
    }

    public async Task<NotificationDto?> GetNotificationByIdAsync(int id)
    {
        var notification = await _notificationRepository.GetByIdAsync(id);
        return notification is null ? null : ToDto(notification);
    }

    public async Task<IEnumerable<NotificationDto>> GetNotificationsByServiceJobAsync(int serviceJobId)
    {
        var notifications = await _notificationRepository.GetAllAsync();
        return notifications.Where(n => n.ServiceJobId == serviceJobId).Select(ToDto);
    }

    public async Task<IEnumerable<NotificationDto>> GetPendingNotificationsAsync()
    {
        var notifications = await _notificationRepository.GetAllAsync();
        return notifications.Where(n => n.Status == NotificationStatus.Pending).Select(ToDto);
    }

    public async Task<NotificationDto> CreateNotificationAsync(NotificationDto notification)
    {
        var entity = new Notification
        {
            ServiceJobId = notification.ServiceJobId,
            Message = notification.Message,
            Status = NotificationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _notificationRepository.AddAsync(entity);
        await _notificationRepository.SaveChangesAsync();

        return ToDto(entity);
    }

    public async Task<NotificationDto> RespondToNotificationAsync(int notificationId, NotificationStatus status)
    {
        var entity = await _notificationRepository.GetByIdAsync(notificationId)
            ?? throw new NotFoundException(nameof(Notification), notificationId);

        entity.Status = status;
        entity.RespondedAt = DateTime.UtcNow;

        _notificationRepository.Update(entity);
        await _notificationRepository.SaveChangesAsync();

        return ToDto(entity);
    }

    private static NotificationDto ToDto(Notification notification) => new()
    {
        Id = notification.Id,
        ServiceJobId = notification.ServiceJobId,
        Message = notification.Message,
        Status = notification.Status,
        CreatedAt = notification.CreatedAt,
        RespondedAt = notification.RespondedAt
    };
}
