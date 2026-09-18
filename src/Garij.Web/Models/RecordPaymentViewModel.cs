using Garij.Application.DTOs;

namespace Garij.Web.Models;

/// <summary>
/// Backs the Record Payment form. The invoice is read-only context; Payment carries the
/// editable fields, so the form can bind with asp-for and pick up the DTO's validation
/// attributes (including the transaction reference length) for unobtrusive client-side
/// validation, instead of using raw inputs that emit no data-val attributes.
/// </summary>
public class RecordPaymentViewModel
{
    public InvoiceDto Invoice { get; set; } = new();

    public PaymentTransactionDto Payment { get; set; } = new();
}
