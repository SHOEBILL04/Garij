namespace Garij.Application.DTOs;

public class RevenueReportDto
{
    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    /// <summary>Total net billed revenue before tax (Invoice.SubTotal).</summary>
    public decimal TotalBilledNet { get; set; }

    /// <summary>Total gross billed revenue including tax (Invoice.TotalAmount).</summary>
    public decimal TotalBilledGross { get; set; }

    /// <summary>Total actual collected cash/payments (PaymentTransaction.Amount).</summary>
    public decimal TotalCollected { get; set; }

    /// <summary>Number of billed non-void invoices issued in the period.</summary>
    public int TotalInvoices { get; set; }

    /// <summary>Average gross value per invoice.</summary>
    public decimal AverageInvoiceValue { get; set; }

    /// <summary>Number of refunded invoices issued in the period.</summary>
    public int RefundedInvoiceCount { get; set; }

    /// <summary>Total gross value of refunded invoices issued in the period.</summary>
    public decimal RefundedGrossAmount { get; set; }

    /// <summary>Backwards-compatible total revenue field (mirrors TotalBilledGross).</summary>
    public decimal TotalRevenue
    {
        get => TotalBilledGross;
        set => TotalBilledGross = value;
    }

    /// <summary>Backwards-compatible invoice count field (mirrors TotalInvoices).</summary>
    public int InvoiceCount
    {
        get => TotalInvoices;
        set => TotalInvoices = value;
    }

    public List<MonthlyBilledRevenueItemDto> MonthlyBilledBreakdown { get; set; } = new();

    public List<MonthlyCollectedRevenueItemDto> MonthlyCollectedBreakdown { get; set; } = new();
}

public class MonthlyBilledRevenueItemDto
{
    public int Year { get; set; }

    public int Month { get; set; }

    public string MonthName { get; set; } = string.Empty;

    public decimal NetRevenue { get; set; }

    public decimal GrossRevenue { get; set; }

    public int InvoiceCount { get; set; }

    public decimal AverageInvoiceValue => InvoiceCount > 0 ? Math.Round(GrossRevenue / InvoiceCount, 2) : 0m;
}

public class MonthlyCollectedRevenueItemDto
{
    public int Year { get; set; }

    public int Month { get; set; }

    public string MonthName { get; set; } = string.Empty;

    public decimal CollectedAmount { get; set; }

    public int TransactionCount { get; set; }
}
