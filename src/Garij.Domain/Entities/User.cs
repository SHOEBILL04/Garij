using Garij.Domain.Enums;

namespace Garij.Domain.Entities;

/// <summary>Staff member (front desk, mechanic, or admin). Linked to an ASP.NET Core Identity account via IdentityUserId.</summary>
public class User
{
    public int Id { get; set; }

    /// <summary>FK to AspNetUsers.Id (ASP.NET Core Identity).</summary>
    public string IdentityUserId { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Unique identifier of the garage/workshop this staff member belongs to.
    /// Ensures staff accounts are isolated to their specific garage.
    /// </summary>
    public string? GarageId { get; set; }

    public ICollection<MechanicAssignment> MechanicAssignments { get; set; } = new List<MechanicAssignment>();
}
