namespace Garij.Application.DTOs;

public class JobServiceDetailDto
{
    public int Id { get; set; }

    public int ServiceJobId { get; set; }

    public int ServiceCatalogId { get; set; }

    /// <summary>Catalogue name at display time; not persisted on the line item.</summary>
    public string ServiceName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal PriceAtBooking { get; set; }
}
