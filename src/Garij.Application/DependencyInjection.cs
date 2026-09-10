using Garij.Application.Interfaces;
using Garij.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Garij.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICustomerVehicleService, CustomerVehicleService>();
        services.AddScoped<IServiceJobService, ServiceJobService>();
        services.AddScoped<IPartsInventoryService, PartsInventoryService>();
        services.AddScoped<IBillingService, BillingService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IReportingService, ReportingService>();
        services.AddScoped<IIntelligenceService, IntelligenceService>();
        services.AddScoped<IProjectPurchaseService, ProjectPurchaseService>();

        return services;
    }
}
