using Garij.Infrastructure.Persistence;
using Garij.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Garij.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<GarijDbContext>(options =>
        {
            if (string.IsNullOrEmpty(connectionString) || connectionString.Contains("Data Source=") || connectionString.Contains(".db"))
            {
                options.UseSqlite(string.IsNullOrEmpty(connectionString) ? "Data Source=Garij.db" : connectionString);
            }
            else if (!OperatingSystem.IsWindows() && (connectionString.Contains("(localdb)") || connectionString.Contains("Server=localhost") || connectionString.Contains("Server=127.0.0.1")))
            {
                // Fallback to SQLite on non-Windows platforms if local SQL Server / LocalDB is specified in dev environment
                options.UseSqlite("Data Source=Garij.db");
            }
            else
            {
                options.UseSqlServer(connectionString);
            }
        });

        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IServiceCatalogRepository, ServiceCatalogRepository>();
        services.AddScoped<IServiceJobRepository, ServiceJobRepository>();
        services.AddScoped<IJobServiceDetailRepository, JobServiceDetailRepository>();
        services.AddScoped<IMechanicAssignmentRepository, MechanicAssignmentRepository>();
        services.AddScoped<IPartRepository, PartRepository>();
        services.AddScoped<IJobPartUsedRepository, JobPartUsedRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IPaymentTransactionRepository, PaymentTransactionRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IProjectPurchaseRepository, ProjectPurchaseRepository>();

        return services;
    }
}
