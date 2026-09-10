using Garij.Domain.Enums;

namespace Garij.Web.Helpers;

public static class StatusBadgeHelper
{
    public static string BadgeClass(JobStatus status) => status switch
    {
        JobStatus.Requested => "app-badge-warning",
        JobStatus.InspectionPending => "app-badge-info",
        JobStatus.CustomerApprovalNeeded => "app-badge-warning",
        JobStatus.InProgress => "app-badge-info",
        JobStatus.Completed => "app-badge-success",
        JobStatus.Cancelled => "app-badge-danger",
        _ => "app-badge-neutral"
    };

    /// <summary>Readable label for a job stage, for use in menus and buttons.</summary>
    public static string DisplayName(JobStatus status) => status switch
    {
        JobStatus.Requested => "Requested",
        JobStatus.InspectionPending => "Inspection",
        JobStatus.CustomerApprovalNeeded => "Customer Approval",
        JobStatus.InProgress => "In Progress",
        JobStatus.Completed => "Completed",
        JobStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    public static string BadgeClass(NotificationStatus status) => status switch
    {
        NotificationStatus.Pending => "app-badge-warning",
        NotificationStatus.Approved => "app-badge-success",
        NotificationStatus.Rejected => "app-badge-danger",
        _ => "app-badge-neutral"
    };

    public static string BadgeClass(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => "app-badge-warning",
        PaymentStatus.PartiallyPaid => "app-badge-info",
        PaymentStatus.Paid => "app-badge-success",
        PaymentStatus.Refunded => "app-badge-neutral",
        _ => "app-badge-neutral"
    };
}
