using Garij.Application.DTOs;
using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Repositories;
using Xunit;

namespace Garij.UnitTests;

/// <summary>
/// The customer-approval queue. A job reaching Completed raises a Pending notification;
/// front desk then approves or rejects it, which is what clears it from the queue badge.
/// </summary>
public class NotificationServiceTests
{
    [Fact]
    public async Task CreateNotificationAsync_ShouldStartPending_EvenWhenTheCallerAsksForAnotherStatus()
    {
        var repository = new FakeNotificationRepository();
        var service = new NotificationService(repository);

        var created = await service.CreateNotificationAsync(new NotificationDto
        {
            ServiceJobId = 7,
            Message = "Job GRJ-2026-0007 has been completed and is ready for review.",
            Status = NotificationStatus.Approved
        });

        // Approval is something a person grants, never something the caller can pre-set.
        Assert.Equal(NotificationStatus.Pending, created.Status);
        Assert.Null(created.RespondedAt);
        Assert.Equal(7, created.ServiceJobId);
    }

    [Fact]
    public async Task CreateNotificationAsync_ShouldStampCreatedAt_AndPersistTheNotification()
    {
        var repository = new FakeNotificationRepository();
        var service = new NotificationService(repository);
        var before = DateTime.UtcNow;

        var created = await service.CreateNotificationAsync(new NotificationDto
        {
            ServiceJobId = 1,
            Message = "Job GRJ-2026-0001 has been completed and is ready for review."
        });

        Assert.InRange(created.CreatedAt, before, DateTime.UtcNow);
        Assert.Equal(1, repository.SaveCount);
        Assert.Single(await repository.GetAllAsync());
    }

    [Theory]
    [InlineData(NotificationStatus.Approved)]
    [InlineData(NotificationStatus.Rejected)]
    public async Task RespondToNotificationAsync_ShouldRecordTheDecision_WithATimestamp(NotificationStatus decision)
    {
        var notification = PendingNotification(id: 1, serviceJobId: 4);
        var repository = new FakeNotificationRepository(notification);
        var service = new NotificationService(repository);
        var before = DateTime.UtcNow;

        var responded = await service.RespondToNotificationAsync(1, decision);

        Assert.Equal(decision, responded.Status);
        Assert.NotNull(responded.RespondedAt);
        Assert.InRange(responded.RespondedAt!.Value, before, DateTime.UtcNow);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task RespondToNotificationAsync_ShouldThrowNotFoundException_WhenTheNotificationDoesNotExist()
    {
        var service = new NotificationService(new FakeNotificationRepository());

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.RespondToNotificationAsync(404, NotificationStatus.Approved));
    }

    [Fact]
    public async Task GetPendingNotificationsAsync_ShouldReturnOnlyTheUnansweredOnes()
    {
        var repository = new FakeNotificationRepository(
            PendingNotification(id: 1, serviceJobId: 1),
            AnsweredNotification(id: 2, serviceJobId: 2, NotificationStatus.Approved),
            AnsweredNotification(id: 3, serviceJobId: 3, NotificationStatus.Rejected),
            PendingNotification(id: 4, serviceJobId: 4));
        var service = new NotificationService(repository);

        var pending = await service.GetPendingNotificationsAsync();

        Assert.Equal(new[] { 1, 4 }, pending.Select(n => n.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task GetPendingNotificationsAsync_ShouldDropTheNotification_OnceItHasBeenAnswered()
    {
        var repository = new FakeNotificationRepository(PendingNotification(id: 1, serviceJobId: 9));
        var service = new NotificationService(repository);

        Assert.Single(await service.GetPendingNotificationsAsync());

        await service.RespondToNotificationAsync(1, NotificationStatus.Approved);

        // This is the queue badge in _Layout emptying out, which is how staff know the
        // approval actually registered.
        Assert.Empty(await service.GetPendingNotificationsAsync());
    }

    [Fact]
    public async Task GetNotificationsByServiceJobAsync_ShouldReturnOnlyThatJobsNotifications()
    {
        var repository = new FakeNotificationRepository(
            PendingNotification(id: 1, serviceJobId: 10),
            AnsweredNotification(id: 2, serviceJobId: 10, NotificationStatus.Approved),
            PendingNotification(id: 3, serviceJobId: 11));
        var service = new NotificationService(repository);

        var forJobTen = await service.GetNotificationsByServiceJobAsync(10);

        // Both statuses, because a job's notification history is not just its open items.
        Assert.Equal(new[] { 1, 2 }, forJobTen.Select(n => n.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task GetNotificationsByServiceJobAsync_ShouldReturnEmpty_WhenTheJobHasRaisedNone()
    {
        var repository = new FakeNotificationRepository(PendingNotification(id: 1, serviceJobId: 10));
        var service = new NotificationService(repository);

        Assert.Empty(await service.GetNotificationsByServiceJobAsync(999));
    }

    [Fact]
    public async Task GetNotificationByIdAsync_ShouldReturnNull_WhenTheNotificationDoesNotExist()
    {
        var service = new NotificationService(new FakeNotificationRepository());

        Assert.Null(await service.GetNotificationByIdAsync(404));
    }

    [Fact]
    public async Task GetAllNotificationsAsync_ShouldReturnEveryNotification_WhateverItsStatus()
    {
        var repository = new FakeNotificationRepository(
            PendingNotification(id: 1, serviceJobId: 1),
            AnsweredNotification(id: 2, serviceJobId: 2, NotificationStatus.Approved),
            AnsweredNotification(id: 3, serviceJobId: 3, NotificationStatus.Rejected));
        var service = new NotificationService(repository);

        Assert.Equal(3, (await service.GetAllNotificationsAsync()).Count());
    }

    private static Notification PendingNotification(int id, int serviceJobId) => new()
    {
        Id = id,
        ServiceJobId = serviceJobId,
        Message = $"Job GRJ-2026-{serviceJobId:D4} has been completed and is ready for review.",
        Status = NotificationStatus.Pending,
        CreatedAt = DateTime.UtcNow.AddMinutes(-10)
    };

    private static Notification AnsweredNotification(int id, int serviceJobId, NotificationStatus status) => new()
    {
        Id = id,
        ServiceJobId = serviceJobId,
        Message = $"Job GRJ-2026-{serviceJobId:D4} has been completed and is ready for review.",
        Status = status,
        CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        RespondedAt = DateTime.UtcNow.AddMinutes(-5)
    };

    private sealed class FakeNotificationRepository : INotificationRepository
    {
        private readonly List<Notification> _items = new();
        private int _nextId = 1;

        public FakeNotificationRepository(params Notification[] seed)
        {
            foreach (var notification in seed)
            {
                _items.Add(notification);
                _nextId = Math.Max(_nextId, notification.Id + 1);
            }
        }

        /// <summary>Lets a test assert the change was actually handed to the database.</summary>
        public int SaveCount { get; private set; }

        public Task<Notification?> GetByIdAsync(int id) =>
            Task.FromResult(_items.FirstOrDefault(n => n.Id == id));

        public Task<IEnumerable<Notification>> GetAllAsync() =>
            Task.FromResult<IEnumerable<Notification>>(_items.ToList());

        public Task AddAsync(Notification entity)
        {
            entity.Id = _nextId++;
            _items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(Notification entity)
        {
        }

        public void Remove(Notification entity) => _items.Remove(entity);

        public Task<int> SaveChangesAsync()
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }
}
