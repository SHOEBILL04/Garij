using System.Net;
using System.Text.RegularExpressions;
using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Garij.IntegrationTests;

/// <summary>
/// Covers defect D-07 (Report 05) through the running app: moving a job into
/// CustomerApprovalNeeded on the job board raises an approval request, and answering it on the
/// notification screen moves the job. Also proves the DI wiring holds - NotificationController
/// now depends on IServiceJobService, which already depends on INotificationService.
///
/// Covers TC-NOT-05, TC-NOT-06 and TC-ACC-06.
/// </summary>
public class NotificationDecisionIntegrationTests : IClassFixture<AuthorizationTestFactory>
{
    private const string GarageId = "default-garij-master";

    private static readonly Regex AntiForgeryTokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly AuthorizationTestFactory _factory;

    public NotificationDecisionIntegrationTests(AuthorizationTestFactory factory)
    {
        _factory = factory;
    }

    private static async Task<string> ExtractAntiForgeryTokenAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        return AntiForgeryTokenPattern.Match(html).Groups[1].Value;
    }

    private async Task<HttpClient> LoginAsAdminAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var loginPage = await client.GetAsync("/Account/Login");
        var token = await ExtractAntiForgeryTokenAsync(loginPage);

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@garij.com",
            ["Password"] = "Admin@12345",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    private T WithDb<T>(Func<GarijDbContext, T> read)
    {
        using var scope = _factory.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<GarijDbContext>());
    }

    private int SeedJob(JobStatus status)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        return WithDb(db =>
        {
            var customer = new Customer
            {
                FullName = "Approval Flow Customer",
                Email = $"apf-{suffix}@test.local",
                PhoneNumber = "+8801711000018",
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
                    LicensePlateNumber = $"AF-{suffix}",
                    Make = "Toyota",
                    Model = "Allion",
                    Year = 2020,
                    Vin = $"VIN-AF-{suffix}",
                    Color = "Silver",
                    GarageId = GarageId
                },
                BookingReference = $"AF-{suffix}",
                JobType = JobType.RoutineService,
                Status = status,
                CreatedAt = DateTime.UtcNow,
                GarageId = GarageId
            };

            db.ServiceJobs.Add(job);
            db.SaveChanges();
            return job.Id;
        });
    }

    private async Task MoveJobOnTheJobBoardAsync(HttpClient client, int jobId, JobStatus newStatus)
    {
        var board = await client.GetAsync("/Mechanic/JobBoard");
        var token = await ExtractAntiForgeryTokenAsync(board);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the job board.");

        var response = await client.PostAsync("/Mechanic/UpdateStatus", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["serviceJobId"] = jobId.ToString(),
            ["newStatus"] = newStatus.ToString(),
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task<HttpResponseMessage> RespondAsync(HttpClient client, int notificationId, NotificationStatus decision)
    {
        var page = await client.GetAsync($"/Notification/Respond/{notificationId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = await ExtractAntiForgeryTokenAsync(page);
        Assert.False(string.IsNullOrEmpty(token), "Could not find __RequestVerificationToken on the respond form.");

        return await client.PostAsync($"/Notification/Respond/{notificationId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["status"] = decision.ToString(),
            ["__RequestVerificationToken"] = token,
        }));
    }

    private Notification SingleNotificationFor(int jobId) =>
        WithDb(db => db.Notifications.AsNoTracking().Single(n => n.ServiceJobId == jobId));

    private JobStatus JobStatusOf(int jobId) =>
        WithDb(db => db.ServiceJobs.AsNoTracking().Single(j => j.Id == jobId).Status);

    [Fact]
    public async Task MovingAJobToCustomerApprovalNeeded_RaisesAnApprovalRequestInTheQueue()
    {
        // TC-NOT-05.
        var client = await LoginAsAdminAsync();
        var jobId = SeedJob(JobStatus.InspectionPending);

        await MoveJobOnTheJobBoardAsync(client, jobId, JobStatus.CustomerApprovalNeeded);

        var notification = SingleNotificationFor(jobId);
        Assert.Equal(NotificationType.CustomerApprovalRequest, notification.Type);
        Assert.Equal(NotificationStatus.Pending, notification.Status);

        var queue = await (await client.GetAsync("/Notification")).Content.ReadAsStringAsync();
        Assert.Contains("needs the customer&#x27;s approval", queue);
    }

    [Fact]
    public async Task ApprovingTheRequest_MovesTheJobToInProgress()
    {
        // TC-NOT-06 / TC-ACC-06.
        var client = await LoginAsAdminAsync();
        var jobId = SeedJob(JobStatus.InspectionPending);
        await MoveJobOnTheJobBoardAsync(client, jobId, JobStatus.CustomerApprovalNeeded);
        var requestId = SingleNotificationFor(jobId).Id;

        var response = await RespondAsync(client, requestId, NotificationStatus.Approved);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(JobStatus.InProgress, JobStatusOf(jobId));
        Assert.Equal(NotificationStatus.Approved, SingleNotificationFor(jobId).Status);

        // Re-opening the job shows the effect.
        var details = await (await client.GetAsync($"/ServiceJob/Details/{jobId}")).Content.ReadAsStringAsync();
        Assert.Contains($">{nameof(JobStatus.InProgress)}</span>", details);
    }

    [Fact]
    public async Task RejectingTheRequest_CancelsTheJob()
    {
        var client = await LoginAsAdminAsync();
        var jobId = SeedJob(JobStatus.InspectionPending);
        await MoveJobOnTheJobBoardAsync(client, jobId, JobStatus.CustomerApprovalNeeded);
        var requestId = SingleNotificationFor(jobId).Id;

        var response = await RespondAsync(client, requestId, NotificationStatus.Rejected);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(JobStatus.Cancelled, JobStatusOf(jobId));
        Assert.Equal(NotificationStatus.Rejected, SingleNotificationFor(jobId).Status);
    }

    [Fact]
    public async Task ApprovingACompletionNotification_LeavesTheJobUnchanged()
    {
        var client = await LoginAsAdminAsync();
        var jobId = SeedJob(JobStatus.Completed);
        var notificationId = WithDb(db =>
        {
            var n = new Notification
            {
                ServiceJobId = jobId,
                Message = "Job has been completed and is ready for review.",
                Type = NotificationType.JobCompleted,
                Status = NotificationStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                GarageId = GarageId
            };
            db.Notifications.Add(n);
            db.SaveChanges();
            return n.Id;
        });

        var response = await RespondAsync(client, notificationId, NotificationStatus.Approved);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(JobStatus.Completed, JobStatusOf(jobId));
        Assert.Equal(NotificationStatus.Approved, SingleNotificationFor(jobId).Status);
    }

    [Fact]
    public async Task ApprovingARequestForAJobAlreadyCancelled_IsRefusedOnTheFormWithTheReason()
    {
        var client = await LoginAsAdminAsync();
        var jobId = SeedJob(JobStatus.InspectionPending);
        await MoveJobOnTheJobBoardAsync(client, jobId, JobStatus.CustomerApprovalNeeded);
        var requestId = SingleNotificationFor(jobId).Id;

        // Staff cancel the job before the customer's answer is entered.
        await MoveJobOnTheJobBoardAsync(client, jobId, JobStatus.Cancelled);

        var response = await RespondAsync(client, requestId, NotificationStatus.Approved);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Cannot change status of a job that is Cancelled.", body);

        Assert.Equal(JobStatus.Cancelled, JobStatusOf(jobId));
        Assert.Equal(NotificationStatus.Pending, SingleNotificationFor(jobId).Status);
    }
}
