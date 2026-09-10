namespace Garij.Application.DTOs;

public class InvoiceLineItemDto
{
    public string Description { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }
}
