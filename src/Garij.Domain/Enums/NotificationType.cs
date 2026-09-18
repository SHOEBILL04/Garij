namespace Garij.Domain.Enums;

/// <summary>
/// What a notification is for. Approving or rejecting a notification only acts on the job
/// for some of these, so the purpose has to be recorded on the row rather than inferred
/// from the message text, which is free-form and may be reworded at any time.
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// Raised when a job reaches Completed. Informational: responding to it records the
    /// acknowledgement and leaves the job alone.
    /// Zero so that notifications written before this field existed read as what they were.
    /// </summary>
    JobCompleted = 0,

    /// <summary>
    /// Raised when a job moves into CustomerApprovalNeeded, asking the customer to decide
    /// whether the work should go ahead. Approving advances the job to InProgress and
    /// rejecting cancels it.
    /// </summary>
    CustomerApprovalRequest = 1
}
