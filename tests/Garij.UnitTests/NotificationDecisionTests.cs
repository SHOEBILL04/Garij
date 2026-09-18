using Garij.Application.Services;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Domain.Exceptions;
using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Garij.UnitTests;

/// <summary>
/// Covers defect D-07 (Report 05): a notification was raised only when a job reached Completed,
/// never when it moved into CustomerApprovalNeeded, and approving or rejecting a notification
/// recorded the decision on the notification row without ever acting on the job.
///
/// Uses the real ServiceJobService and NotificationService over one SQLite database rather than
/// fakes, because what is under test is the hand-off between the two - the decision on one row
/// has to reach the other.
///
/// Covers TC-NOT-05, TC-NOT-06 and TC-ACC-06.
/// </summary>
public class NotificationDecisionTests : IDisposable
{
    private const string GarageId = "default-garij-master";

    private readonly SqliteConnection _connection;

    public NotificationDecisionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private GarijDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GarijDbContext>().UseSqlite(_connection).Options);

    private static ServiceJobService ServiceFor(GarijDbContext context) =>
        new(
            new ServiceJobRepository(context),
            new VehicleRepository(context),
            new UserRepository(context),
            new MechanicAssignmentRepository(context),
            new NotificationService(new NotificationRepository(context)));

    private async Task<int> SeedJobAsync(JobStatus status, bool withLoggedService = false)
    {
        using var context = NewContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var customer = new Customer
        {
            FullName = "Decision Test Customer",
            Email = $"dec-{suffix}@test.local",
            PhoneNumber = "+8801711000017",
            Address = "Dhaka",
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        var job = new ServiceJob
        {
            Customer = customer,
            Vehicle = new Vehicle
            {
                Customer = customer,
                LicensePlateNumber = $"DC-{suffix}",
                Make = "Toyota",
                Model = "Allion",
                Year = 2020,
                Vin = $"VIN-{suffix}",
                Color = "Silver",
                GarageId = GarageId
            },
            BookingReference = $"DC-{suffix}",
            JobType = JobType.RoutineService,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = status == JobStatus.Completed ? DateTime.UtcNow : null,
            GarageId = GarageId
        };

        if (withLoggedService)
        {
            job.JobServiceDetails.Add(new JobServiceDetail
            {
                ServiceJob = job,
                ServiceCatalog = new ServiceCatalog { Name = $"Diagnostic {suffix}", Description = "Full diagnostic", BasePrice = 80m, EstimatedDurationMinutes = 60 },
                Quantity = 1,
                PriceAtBooking = 80m
            });
        }

        context.ServiceJobs.Add(job);
        await context.SaveChangesAsync();
        return job.Id;
    }

    private async Task<int> SeedNotificationAsync(int jobId, NotificationType type, NotificationStatus status = NotificationStatus.Pending)
    {
        using var context = NewContext();
        var notification = new Notification
        {
            ServiceJobId = jobId,
            Message = "Seeded notification.",
            Type = type,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            GarageId = GarageId
        };

        context.Notifications.Add(notification);
        await context.SaveChangesAsync();
        return notification.Id;
    }

    private JobStatus ReadJobStatus(int jobId)
    {
        using var context = NewContext();
        return context.ServiceJobs.AsNoTracking().Single(j => j.Id == jobId).Status;
    }

    private Notification ReadNotification(int notificationId)
    {
        using var context = NewContext();
        return context.Notifications.AsNoTracking().Single(n => n.Id == notificationId);
    }

    private List<Notification> NotificationsFor(int jobId)
    {
        using var context = NewContext();
        return context.Notifications.AsNoTracking().Where(n => n.ServiceJobId == jobId).ToList();
    }

    // =====================================================================================
    // (a) TC-NOT-05: moving a job into CustomerApprovalNeeded raises an approval request.
    // =====================================================================================

    [Fact]
    public async Task UpdateServiceJobStatusAsync_RaisesAnApprovalRequest_WhenTheJobMovesIntoCustomerApprovalNeeded()
    {
        var jobId = await SeedJobAsync(JobStatus.InspectionPending);

        using (var context = NewContext())
        {
            await ServiceFor(context).UpdateServiceJobStatusAsync(jobId, JobStatus.CustomerApprovalNeeded);
        }

        var notification = Assert.Single(NotificationsFor(jobId));
        Assert.Equal(NotificationType.CustomerApprovalRequest, notification.Type);
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Contains("needs the customer's approval", notification.Message);
        Assert.DoesNotContain("has been completed", notification.Message);
    }

    [Fact]
    public async Task UpdateServiceJobAsync_RaisesAnApprovalRequest_WhenTheEditFormMovesTheJobIntoCustomerApprovalNeeded()
    {
        // The edit form is the second path that changes status; the completion notification is
        // raised from both, so the approval request must be too.
        var jobId = await SeedJobAsync(JobStatus.Requested);

        using (var context = NewContext())
        {
            var service = ServiceFor(context);
            var job = await service.GetServiceJobByIdAsync(jobId);
            job!.Status = JobStatus.CustomerApprovalNeeded;
            await service.UpdateServiceJobAsync(job);
        }

        var notification = Assert.Single(NotificationsFor(jobId));
        Assert.Equal(NotificationType.CustomerApprovalRequest, notification.Type);
    }

    [Fact]
    public async Task UpdateServiceJobStatusAsync_DoesNotRaiseASecondRequest_WhenTheJobIsAlreadyWaitingForApproval()
    {
        var jobId = await SeedJobAsync(JobStatus.CustomerApprovalNeeded);

        using (var context = NewContext())
        {
            await ServiceFor(context).UpdateServiceJobStatusAsync(jobId, JobStatus.CustomerApprovalNeeded);
        }

        Assert.Empty(NotificationsFor(jobId));
    }

    [Fact]
    public async Task UpdateServiceJobStatusAsync_StillRaisesTheCompletionNotification_Unchanged()
    {
        // The completion notification's trigger and message are out of scope and must be as before.
        var jobId = await SeedJobAsync(JobStatus.InProgress, withLoggedService: true);

        using (var context = NewContext())
        {
            await ServiceFor(context).UpdateServiceJobStatusAsync(jobId, JobStatus.Completed);
        }

        var notification = Assert.Single(NotificationsFor(jobId));
        Assert.Equal(NotificationType.JobCompleted, notification.Type);
        Assert.EndsWith("has been completed and is ready for review.", notification.Message);
    }

    // =====================================================================================
    // (b) TC-NOT-06 / TC-ACC-06: approving the request moves the job to InProgress.
    // =====================================================================================

    [Fact]
    public async Task RespondToNotificationAsync_Approving_AdvancesTheJobToInProgress()
    {
        var jobId = await SeedJobAsync(JobStatus.InspectionPending);

        using (var context = NewContext())
        {
            await ServiceFor(context).UpdateServiceJobStatusAsync(jobId, JobStatus.CustomerApprovalNeeded);
        }

        var requestId = Assert.Single(NotificationsFor(jobId)).Id;

        using (var context = NewContext())
        {
            var responded = await ServiceFor(context).RespondToNotificationAsync(requestId, NotificationStatus.Approved);
            Assert.Equal(NotificationStatus.Approved, responded.Status);
        }

        Assert.Equal(JobStatus.InProgress, ReadJobStatus(jobId));

        var stored = ReadNotification(requestId);
        Assert.Equal(NotificationStatus.Approved, stored.Status);
        Assert.NotNull(stored.RespondedAt);
    }

    // =====================================================================================
    // (c) Rejecting the request moves the job to Cancelled.
    // =====================================================================================

    [Fact]
    public async Task RespondToNotificationAsync_Rejecting_CancelsTheJob()
    {
        var jobId = await SeedJobAsync(JobStatus.InspectionPending);

        using (var context = NewContext())
        {
            await ServiceFor(context).UpdateServiceJobStatusAsync(jobId, JobStatus.CustomerApprovalNeeded);
        }

        var requestId = Assert.Single(NotificationsFor(jobId)).Id;

        using (var context = NewContext())
        {
            await ServiceFor(context).RespondToNotificationAsync(requestId, NotificationStatus.Rejected);
        }

        Assert.Equal(JobStatus.Cancelled, ReadJobStatus(jobId));
        Assert.Equal(NotificationStatus.Rejected, ReadNotification(requestId).Status);
    }

    // =====================================================================================
    // (d) A completion notification's decision is recorded but never moves the job.
    // =====================================================================================

    [Theory]
    [InlineData(NotificationStatus.Approved)]
    [InlineData(NotificationStatus.Rejected)]
    public async Task RespondToNotificationAsync_OnACompletionNotification_LeavesTheJobCompleted(NotificationStatus decision)
    {
        var jobId = await SeedJobAsync(JobStatus.Completed, withLoggedService: true);
        var notificationId = await SeedNotificationAsync(jobId, NotificationType.JobCompleted);

        using (var context = NewContext())
        {
            await ServiceFor(context).RespondToNotificationAsync(notificationId, decision);
        }

        Assert.Equal(JobStatus.Completed, ReadJobStatus(jobId));
        Assert.Equal(decision, ReadNotification(notificationId).Status);
    }

    [Theory]
    [InlineData(NotificationStatus.Approved)]
    [InlineData(NotificationStatus.Rejected)]
    public async Task RespondToNotificationAsync_UsesTheNotificationType_NotTheJobsCurrentStatus(NotificationStatus decision)
    {
        // A completion-type notification on a job that happens to sit in CustomerApprovalNeeded
        // still has no effect: the purpose recorded on the notification decides, not the job.
        var jobId = await SeedJobAsync(JobStatus.CustomerApprovalNeeded);
        var notificationId = await SeedNotificationAsync(jobId, NotificationType.JobCompleted);

        using (var context = NewContext())
        {
            await ServiceFor(context).RespondToNotificationAsync(notificationId, decision);
        }

        Assert.Equal(JobStatus.CustomerApprovalNeeded, ReadJobStatus(jobId));
    }

    // =====================================================================================
    // (e) The normal transition rules still apply - a decision cannot move a terminal job.
    // =====================================================================================

    [Theory]
    [InlineData(JobStatus.Completed, NotificationStatus.Approved)]
    [InlineData(JobStatus.Completed, NotificationStatus.Rejected)]
    [InlineData(JobStatus.Cancelled, NotificationStatus.Approved)]
    public async Task RespondToNotificationAsync_CannotMoveAJobThatIsAlreadyTerminal(JobStatus terminalStatus, NotificationStatus decision)
    {
        var jobId = await SeedJobAsync(terminalStatus, withLoggedService: true);
        var requestId = await SeedNotificationAsync(jobId, NotificationType.CustomerApprovalRequest);

        using (var context = NewContext())
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                ServiceFor(context).RespondToNotificationAsync(requestId, decision));

            Assert.Equal("BR-007", ex.RuleCode);
        }

        // Neither side changed: the job keeps its terminal status, and the decision was not
        // recorded, so the notification does not claim an outcome the job never took.
        Assert.Equal(terminalStatus, ReadJobStatus(jobId));

        var notification = ReadNotification(requestId);
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Null(notification.RespondedAt);
    }

    [Fact]
    public async Task RespondToNotificationAsync_CannotAnswerTheSameApprovalRequestTwice()
    {
        // Otherwise a stale reject after an approval would cancel a job that is mid-repair, which
        // the transition rules alone allow (InProgress -> Cancelled is a legal move).
        var jobId = await SeedJobAsync(JobStatus.CustomerApprovalNeeded);
        var requestId = await SeedNotificationAsync(jobId, NotificationType.CustomerApprovalRequest);

        using (var context = NewContext())
        {
            await ServiceFor(context).RespondToNotificationAsync(requestId, NotificationStatus.Approved);
        }

        using (var context = NewContext())
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                ServiceFor(context).RespondToNotificationAsync(requestId, NotificationStatus.Rejected));

            Assert.Equal("BR-014", ex.RuleCode);
        }

        Assert.Equal(JobStatus.InProgress, ReadJobStatus(jobId));
        Assert.Equal(NotificationStatus.Approved, ReadNotification(requestId).Status);
    }

    [Fact]
    public async Task RespondToNotificationAsync_RejectsAPendingDecision()
    {
        var jobId = await SeedJobAsync(JobStatus.CustomerApprovalNeeded);
        var requestId = await SeedNotificationAsync(jobId, NotificationType.CustomerApprovalRequest);

        using (var context = NewContext())
        {
            await Assert.ThrowsAsync<ValidationException>(() =>
                ServiceFor(context).RespondToNotificationAsync(requestId, NotificationStatus.Pending));
        }

        Assert.Equal(JobStatus.CustomerApprovalNeeded, ReadJobStatus(jobId));
    }

    [Fact]
    public async Task RespondToNotificationAsync_ThrowsNotFound_ForAnUnknownNotification()
    {
        using var context = NewContext();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            ServiceFor(context).RespondToNotificationAsync(99999, NotificationStatus.Approved));
    }
}
