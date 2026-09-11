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
    private readonly ICurrentGarageService? _currentGarageService;

    public NotificationService(
        INotificationRepository notificationRepository,
        ICurrentGarageService? currentGarageService = null)
    {
        _notificationRepository = notificationRepository;
        _currentGarageService = currentGarageService;
    }

    private async Task<string> ResolveGarageIdAsync(string? explicitGarageId = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitGarageId))
        {
            return explicitGarageId;
        }

        if (_currentGarageService != null)
        {
            var id = await _currentGarageService.GetCurrentGarageIdAsync();
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }

        return "default-garij-master";
    }

    public async Task<IEnumerable<NotificationDto>> GetAllNotificationsAsync()
    {
        var garageId = await ResolveGarageIdAsync();
        var notifications = await _notificationRepository.GetAllAsync();
        return notifications
            .Where(n => (n.GarageId ?? "default-garij-master") == garageId)
            .Select(ToDto);
    }

    public async Task<NotificationDto?> GetNotificationByIdAsync(int id)
    {
        var garageId = await ResolveGarageIdAsync();
        var notification = await _notificationRepository.GetByIdAsync(id);
        if (notification is null || (notification.GarageId ?? "default-garij-master") != garageId)
        {
            return null;
        }

        return ToDto(notification);
    }

    public async Task<IEnumerable<NotificationDto>> GetNotificationsByServiceJobAsync(int serviceJobId)
    {
        var garageId = await ResolveGarageIdAsync();
        var notifications = await _notificationRepository.GetAllAsync();
        return notifications
            .Where(n => n.ServiceJobId == serviceJobId && (n.GarageId ?? "default-garij-master") == garageId)
            .Select(ToDto);
    }

    public async Task<IEnumerable<NotificationDto>> GetPendingNotificationsAsync()
    {
        var garageId = await ResolveGarageIdAsync();
        var notifications = await _notificationRepository.GetAllAsync();
        return notifications
            .Where(n => n.Status == NotificationStatus.Pending && (n.GarageId ?? "default-garij-master") == garageId)
            .Select(ToDto);
    }

    public async Task<NotificationDto> CreateNotificationAsync(NotificationDto notification)
    {
        var garageId = await ResolveGarageIdAsync(notification.GarageId);
        var entity = new Notification
        {
            GarageId = garageId,
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
        var garageId = await ResolveGarageIdAsync();
        var entity = await _notificationRepository.GetByIdAsync(notificationId)
            ?? throw new NotFoundException(nameof(Notification), notificationId);

        if ((entity.GarageId ?? "default-garij-master") != garageId)
        {
            throw new NotFoundException(nameof(Notification), notificationId);
        }

        entity.Status = status;
        entity.RespondedAt = DateTime.UtcNow;

        _notificationRepository.Update(entity);
        await _notificationRepository.SaveChangesAsync();

        return ToDto(entity);
    }

    private static NotificationDto ToDto(Notification notification) => new()
    {
        Id = notification.Id,
        GarageId = notification.GarageId,
        ServiceJobId = notification.ServiceJobId,
        Message = notification.Message,
        Status = notification.Status,
        CreatedAt = notification.CreatedAt,
        RespondedAt = notification.RespondedAt
    };
}
