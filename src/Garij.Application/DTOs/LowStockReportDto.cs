namespace Garij.Application.DTOs;

/// <summary>One row of the Low Stock report: a part at or below its reorder level.</summary>
public class LowStockReportDto
{
    public int PartId { get; set; }

    public string PartName { get; set; } = string.Empty;

    public string PartNumber { get; set; } = string.Empty;

    public int CurrentStock { get; set; }

    public int ReorderLevel { get; set; }

    public decimal UnitPrice { get; set; }

    /// <summary>Units below the reorder level; zero when stock sits exactly at it.</summary>
    public int ShortfallBelowReorderLevel => Math.Max(0, ReorderLevel - CurrentStock);

    public bool IsOutOfStock => CurrentStock <= 0;
}
