using Garij.Infrastructure.Persistence;
using Garij.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Garij.IntegrationTests;

/// <summary>
/// Hosts the real Garij.Web app (Program.cs untouched) against an in-memory SQLite
/// database instead of the SQL Server connection string in appsettings.json, so
/// authorization tests can run without a real database. DbSeeder still runs as part
/// of normal app startup, so the three seeded staff accounts (Admin/FrontDesk/Mechanic)
/// are available against this in-memory database exactly as they are at runtime.
/// Uses AccountController (a public type in the Garij.Web assembly) instead of Program
/// as the generic argument, since Program is generated as an internal type by top-level
/// statements and Garij.Web has no InternalsVisibleTo for the test assembly.
/// </summary>
public class AuthorizationTestFactory : WebApplicationFactory<AccountController>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AuthorizationTestFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // AddDbContext (EF Core 10) registers the original UseSqlServer configuration
            // as an IDbContextOptionsConfiguration<GarijDbContext> descriptor, separate from
            // DbContextOptions<GarijDbContext> itself. Removing only the latter still leaves
            // the SqlServer-configuring delegate registered, so EF Core sees both SqlServer
            // and Sqlite configured on the same context and refuses to pick one. All four
            // descriptors AddInfrastructure's AddDbContext call added must go before we
            // re-add a Sqlite-only registration.
            services.RemoveAll<IDbContextOptionsConfiguration<GarijDbContext>>();
            services.RemoveAll<DbContextOptions<GarijDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<GarijDbContext>();
            services.AddDbContext<GarijDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
