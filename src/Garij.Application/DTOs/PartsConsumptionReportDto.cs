namespace Garij.Application.DTOs;

public class PartsConsumptionReportDto
{
    public int PartId { get; set; }

    public string PartName { get; set; } = string.Empty;

    public string PartNumber { get; set; } = string.Empty;

    public int TotalQuantityUsed { get; set; }

    public decimal TotalCost { get; set; }

    public int CurrentStock { get; set; }

    public int ReorderLevel { get; set; }

    public bool IsLowStock { get; set; }
}
