using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Garij.Infrastructure.SeedData;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GarijDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<GarijDbContext>>();

        try
        {
            await context.Database.EnsureCreatedAsync();

            // 1. Seed Roles
            string[] roles = Enum.GetNames<UserRole>();
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                    logger.LogInformation("Seeded Identity role: {Role}", role);
                }
            }

            // 2. Seed Default Staff Accounts (Admin, FrontDesk, Mechanic)
            var defaultAccounts = new[]
            {
                (Email: "admin@garij.com", Password: "Admin@12345", Name: "System Administrator", Phone: "+1234567890", Role: UserRole.Admin),
                (Email: "frontdesk@garij.com", Password: "Staff@12345", Name: "Front Desk Staff", Phone: "+1234567891", Role: UserRole.FrontDesk),
                (Email: "mechanic@garij.com", Password: "Mechanic@12345", Name: "Lead Mechanic", Phone: "+1234567892", Role: UserRole.Mechanic)
            };

            foreach (var acc in defaultAccounts)
            {
                var existingUser = await userManager.FindByEmailAsync(acc.Email);
                if (existingUser == null)
                {
                    var identityUser = new IdentityUser
                    {
                        UserName = acc.Email,
                        Email = acc.Email,
                        EmailConfirmed = true
                    };

                    var createResult = await userManager.CreateAsync(identityUser, acc.Password);
                    if (createResult.Succeeded)
                    {
                        await userManager.AddToRoleAsync(identityUser, acc.Role.ToString());
                        logger.LogInformation("Seeded Default Identity User: {Email} ({Role})", acc.Email, acc.Role);

                        if (!await context.StaffUsers.AnyAsync(u => u.Email == acc.Email))
                        {
                            context.StaffUsers.Add(new User
                            {
                                IdentityUserId = identityUser.Id,
                                FullName = acc.Name,
                                Email = acc.Email,
                                PhoneNumber = acc.Phone,
                                Role = acc.Role,
                                CreatedAt = DateTime.UtcNow
                            });
                            await context.SaveChangesAsync();
                        }
                    }
                }
            }

            // 3. Seed Service Catalog Items
            if (!await context.ServiceCatalogs.AnyAsync())
            {
                context.ServiceCatalogs.AddRange(
                    new ServiceCatalog { Name = "Oil Change & Filter Replacement", Description = "Full synthetic oil change including filter replacement", BasePrice = 50.00m, EstimatedDurationMinutes = 45 },
                    new ServiceCatalog { Name = "Brake Pad Replacement", Description = "Front or rear brake pad replacement and inspection", BasePrice = 120.00m, EstimatedDurationMinutes = 90 },
                    new ServiceCatalog { Name = "Tire Rotation & Balancing", Description = "Rotate 4 tires and balance wheels", BasePrice = 40.00m, EstimatedDurationMinutes = 40 },
                    new ServiceCatalog { Name = "Engine Computer Diagnostic", Description = "Full OBD-II diagnostic scan and troubleshooting report", BasePrice = 80.00m, EstimatedDurationMinutes = 60 },
                    new ServiceCatalog { Name = "AC Service & Gas Refill", Description = "Air conditioning pressure check, leak check, and R134a refill", BasePrice = 95.00m, EstimatedDurationMinutes = 60 },
                    new ServiceCatalog { Name = "Wheel Alignment", Description = "4-wheel laser alignment adjustment", BasePrice = 65.00m, EstimatedDurationMinutes = 50 },
                    new ServiceCatalog { Name = "Full Synthetic Transmission Fluid Service", Description = "Flush and replace transmission fluid with synthetic ATF", BasePrice = 150.00m, EstimatedDurationMinutes = 90 },
                    new ServiceCatalog { Name = "Spark Plug & Ignition Maintenance", Description = "Replace set of spark plugs and check ignition coils", BasePrice = 110.00m, EstimatedDurationMinutes = 75 },
                    new ServiceCatalog { Name = "Suspension & Steering Inspection", Description = "Inspect shock absorbers, struts, ball joints, and tie rods", BasePrice = 85.00m, EstimatedDurationMinutes = 60 },
                    new ServiceCatalog { Name = "Battery & Electrical System Check", Description = "Test battery load, alternator output, and starter motor draw", BasePrice = 45.00m, EstimatedDurationMinutes = 30 }
                );
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded initial Service Catalog items.");
            }

            // 4. Seed Stock Parts
            if (!await context.Parts.AnyAsync())
            {
                context.Parts.AddRange(
                    new Part { Name = "Synthetic Engine Oil 5W-30 (1L)", PartNumber = "OIL-5W30", UnitPrice = 25.00m, QuantityInStock = 50, ReorderLevel = 10 },
                    new Part { Name = "Premium Brake Pads Front Set", PartNumber = "BRK-PAD-F", UnitPrice = 60.00m, QuantityInStock = 30, ReorderLevel = 5 },
                    new Part { Name = "Premium Brake Pads Rear Set", PartNumber = "BRK-PAD-R", UnitPrice = 55.00m, QuantityInStock = 28, ReorderLevel = 5 },
                    new Part { Name = "Oil Filter Type-A", PartNumber = "FLT-OIL-A", UnitPrice = 12.00m, QuantityInStock = 40, ReorderLevel = 8 },
                    new Part { Name = "Air Filter Universal", PartNumber = "FLT-AIR-U", UnitPrice = 18.00m, QuantityInStock = 25, ReorderLevel = 5 },
                    new Part { Name = "Cabin Air Filter", PartNumber = "FLT-CAB-U", UnitPrice = 15.00m, QuantityInStock = 22, ReorderLevel = 5 },
                    new Part { Name = "Spark Plug Set Platinum", PartNumber = "SPK-PLG-P", UnitPrice = 45.00m, QuantityInStock = 20, ReorderLevel = 5 },
                    new Part { Name = "R134a Refrigerant Gas Canister", PartNumber = "REF-134A", UnitPrice = 35.00m, QuantityInStock = 15, ReorderLevel = 3 },
                    new Part { Name = "Car Battery 12V 60Ah", PartNumber = "BAT-12V60", UnitPrice = 110.00m, QuantityInStock = 12, ReorderLevel = 3 },
                    new Part { Name = "Timing Belt Kit", PartNumber = "BLT-TMG-K", UnitPrice = 85.00m, QuantityInStock = 10, ReorderLevel = 2 },
                    new Part { Name = "Serpentine Drive Belt", PartNumber = "BLT-SRP-D", UnitPrice = 22.00m, QuantityInStock = 18, ReorderLevel = 4 },
                    new Part { Name = "Front Shock Absorber", PartNumber = "SHK-ABS-F", UnitPrice = 70.00m, QuantityInStock = 16, ReorderLevel = 4 },
                    new Part { Name = "Rear Shock Absorber", PartNumber = "SHK-ABS-R", UnitPrice = 65.00m, QuantityInStock = 16, ReorderLevel = 4 },
                    new Part { Name = "Radiator Coolant (1L)", PartNumber = "CLT-RAD-1", UnitPrice = 14.00m, QuantityInStock = 35, ReorderLevel = 8 },
                    new Part { Name = "Wiper Blade Set", PartNumber = "WPR-BLD-S", UnitPrice = 20.00m, QuantityInStock = 24, ReorderLevel = 6 },
                    new Part { Name = "Headlight Bulb H4", PartNumber = "BLB-H4-HD", UnitPrice = 8.00m, QuantityInStock = 45, ReorderLevel = 10 },
                    new Part { Name = "Fuel Filter", PartNumber = "FLT-FUEL-A", UnitPrice = 16.00m, QuantityInStock = 20, ReorderLevel = 5 },
                    new Part { Name = "Transmission Fluid (1L)", PartNumber = "FLD-TRANS-1", UnitPrice = 19.00m, QuantityInStock = 30, ReorderLevel = 6 },
                    new Part { Name = "Radiator Hose Upper", PartNumber = "HOS-RAD-U", UnitPrice = 17.00m, QuantityInStock = 14, ReorderLevel = 3 },
                    new Part { Name = "Wheel Alignment Kit", PartNumber = "ALN-WHL-K", UnitPrice = 40.00m, QuantityInStock = 8, ReorderLevel = 2 }
                );
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded initial Stock Parts.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }
}
