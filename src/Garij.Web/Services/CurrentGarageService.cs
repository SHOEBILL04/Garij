using System.Security.Claims;
using Garij.Application.Interfaces;
using Garij.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Garij.Web.Services;

public class CurrentGarageService : ICurrentGarageService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly GarijDbContext _context;

    public CurrentGarageService(IHttpContextAccessor httpContextAccessor, GarijDbContext context)
    {
        _httpContextAccessor = httpContextAccessor;
        _context = context;
    }

    public async Task<string> GetCurrentGarageIdAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity == null || !user.Identity.IsAuthenticated)
        {
            return "PUBLIC_ANONYMOUS";
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity.Name;

        if (string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(email))
        {
            return "default-garij-master";
        }

        var staff = await _context.StaffUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u =>
                (!string.IsNullOrEmpty(userId) && u.IdentityUserId == userId) ||
                (!string.IsNullOrEmpty(email) && u.Email == email));

        return staff?.GarageId ?? "default-garij-master";
    }
}
