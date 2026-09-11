namespace Garij.Domain.Entities;

public class Vehicle
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    /// <summary>Unique across all vehicles.</summary>
    public string LicensePlateNumber { get; set; } = string.Empty;

    public string Make { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int Year { get; set; }

    public string Vin { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    public string? GarageId { get; set; }

    public ICollection<ServiceJob> ServiceJobs { get; set; } = new List<ServiceJob>();
}
