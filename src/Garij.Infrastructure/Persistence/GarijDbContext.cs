using Garij.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Garij.Infrastructure.Persistence;

/// <summary>
/// Combines ASP.NET Core Identity (login accounts, roles) with the Garij domain model in a single database.
/// The staff "User" entity is exposed as <see cref="StaffUsers"/> to avoid colliding with IdentityDbContext.Users.
/// </summary>
public class GarijDbContext : IdentityDbContext<IdentityUser>
{
    public GarijDbContext(DbContextOptions<GarijDbContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<User> StaffUsers => Set<User>();

    public DbSet<ServiceCatalog> ServiceCatalogs => Set<ServiceCatalog>();

    public DbSet<ServiceJob> ServiceJobs => Set<ServiceJob>();

    public DbSet<JobServiceDetail> JobServiceDetails => Set<JobServiceDetail>();

    public DbSet<MechanicAssignment> MechanicAssignments => Set<MechanicAssignment>();

    public DbSet<Part> Parts => Set<Part>();

    public DbSet<JobPartUsed> JobPartsUsed => Set<JobPartUsed>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();

    public DbSet<AiRequestLog> AiRequestLogs => Set<AiRequestLog>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<ProjectPurchase> ProjectPurchases => Set<ProjectPurchase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GarijDbContext).Assembly);
    }
}
