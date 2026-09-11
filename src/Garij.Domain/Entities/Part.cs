namespace Garij.Domain.Entities;

public class Part
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string PartNumber { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }

    public int QuantityInStock { get; set; }

    public int ReorderLevel { get; set; }

    public string? GarageId { get; set; }

    /// <summary>
    /// Optimistic concurrency token, re-stamped on every stock mutation. Without it two
    /// mechanics logging parts at the same moment both read the same QuantityInStock and
    /// the second write silently overwrites the first (lost update).
    /// A Guid is used rather than a SQL Server rowversion because the project runs on
    /// SQLite in development and SQL Server in production, and rowversion is not portable.
    /// </summary>
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    public ICollection<JobPartUsed> JobPartsUsed { get; set; } = new List<JobPartUsed>();
}
