using Garij.Domain.Enums;

namespace Garij.Application.DTOs;

public class MechanicAssignmentDto
{
    public int Id { get; set; }

    public int ServiceJobId { get; set; }

    public int UserId { get; set; }

    public string MechanicName { get; set; } = string.Empty;

    public RoleInJob RoleInJob { get; set; }

    public DateTime AssignedAt { get; set; }
}
