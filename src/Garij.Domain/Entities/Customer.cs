namespace Garij.Domain.Entities;

public class Customer
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string? GarageId { get; set; }

    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();

    public ICollection<ServiceJob> ServiceJobs { get; set; } = new List<ServiceJob>();
}
