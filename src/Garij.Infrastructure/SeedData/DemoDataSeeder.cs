using Garij.Domain.Entities;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Garij.Infrastructure.SeedData;

/// <summary>
/// Presentation dataset: 24 customers, 36 vehicles and 60 service jobs spread across every
/// status, with the mechanic assignments, parts, invoices, payments and notifications that
/// make the dashboards, reports and low-stock alerts show real numbers during the demo.
///
/// Kept out of <see cref="DbSeeder"/> so the two can be reviewed and reverted separately,
/// and driven by a fixed random seed so every machine demoes identical data.
/// </summary>
public static class DemoDataSeeder
{
    private const int RandomSeed = 20260909;
    private const decimal TaxRatePercent = 15m;

    /// <summary>Job counts per status, summing to 60 and weighted towards Completed so the revenue reports have something to chart.</summary>
    private static readonly (JobStatus Status, int Count)[] StatusPlan =
    [
        (JobStatus.Requested, 8),
        (JobStatus.InspectionPending, 7),
        (JobStatus.CustomerApprovalNeeded, 6),
        (JobStatus.InProgress, 10),
        (JobStatus.Completed, 24),
        (JobStatus.Cancelled, 5)
    ];

    private static readonly string[] FirstNames =
    [
        "Tanvir", "Nusrat", "Mahmudul", "Farhana", "Shakib", "Ishrat", "Arif", "Sadia",
        "Imran", "Rumana", "Zahid", "Tasnim", "Nafis", "Sabrina", "Rashed", "Maliha",
        "Sohel", "Jarin", "Mizanur", "Afsana", "Kamrul", "Nabila", "Rezaul", "Sharmin"
    ];

    private static readonly string[] LastNames =
    [
        "Islam", "Ahmed", "Rahman", "Hossain", "Chowdhury", "Karim",
        "Siddique", "Akter", "Bhuiyan", "Mahmud", "Haque", "Sarker"
    ];

    private static readonly string[] Areas =
    [
        "Sonadanga, Khulna", "Khalishpur, Khulna", "Dhanmondi, Dhaka", "Uttara, Dhaka",
        "Gulshan, Dhaka", "Agrabad, Chattogram", "Zindabazar, Sylhet", "Boalia, Rajshahi"
    ];

    private static readonly string[] PlatePrefixes = ["DHA", "KHL", "CTG", "SYL", "RAJ", "BAR"];

    private static readonly (string Make, string Model)[] Models =
    [
        ("Toyota", "Corolla"), ("Toyota", "Axio"), ("Toyota", "Premio"), ("Toyota", "Allion"),
        ("Honda", "Civic"), ("Honda", "Fit"), ("Honda", "Grace"),
        ("Nissan", "Sunny"), ("Nissan", "X-Trail"),
        ("Mitsubishi", "Lancer"), ("Mitsubishi", "Pajero"),
        ("Mazda", "CX-5"), ("Mazda", "Demio"),
        ("Hyundai", "Tucson"), ("Kia", "Sportage"), ("Suzuki", "Swift")
    ];

    private static readonly string[] Colors =
    [
        "White", "Black", "Silver", "Red", "Blue", "Grey", "Pearl White", "Maroon"
    ];

    private static readonly string[] DiagnosticNotes =
    [
        "Customer reports intermittent squealing from the front wheels under braking.",
        "Engine idles rough when cold; suspect ignition coils.",
        "AC blows warm after roughly twenty minutes of driving.",
        "Check engine light on; OBD-II scan pulled a lean-mixture code.",
        "Vibration through the steering wheel above 80 km/h.",
        "Battery drained twice this week; alternator output to be tested.",
        "Oil change overdue by 2,000 km per the service book.",
        "Suspension knocking over speed bumps on the rear left."
    ];

    private static readonly (string Email, string Name, string Phone)[] DemoMechanics =
    [
        ("tanvir.mechanic@garij.com", "Tanvir Islam", "+8801711000101"),
        ("shakib.mechanic@garij.com", "Shakib Ahmed", "+8801711000102"),
        ("rashed.mechanic@garij.com", "Rashed Karim", "+8801711000103"),
        ("mizanur.mechanic@garij.com", "Mizanur Rahman", "+8801711000104"),
        ("kamrul.mechanic@garij.com", "Kamrul Hossain", "+8801711000105")
    ];

    public static async Task SeedAsync(GarijDbContext context, UserManager<IdentityUser> userManager, ILogger logger)
    {
        // Customers are the anchor: if any exist, either a previous run seeded the demo set
        // or real data has been entered, and either way this must not pile more on top.
        if (await context.Customers.AnyAsync())
        {
            return;
        }

        var catalog = await context.ServiceCatalogs.ToListAsync();
        var parts = await context.Parts.ToListAsync();
        if (catalog.Count == 0 || parts.Count == 0)
        {
            logger.LogWarning("Skipping demo dataset: service catalog and parts must be seeded first.");
            return;
        }

        var random = new Random(RandomSeed);

        var mechanics = await SeedMechanicsAsync(context, userManager, logger);
        if (mechanics.Count == 0)
        {
            logger.LogWarning("Skipping demo dataset: no mechanics available to assign to jobs.");
            return;
        }

        var vehicles = SeedCustomersAndVehicles(context, random);
        await context.SaveChangesAsync();

        SeedServiceJobs(context, random, vehicles, mechanics, catalog, parts);
        DriveSomePartsBelowReorderLevel(parts);
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Seeded demo dataset: {Customers} customers, {Vehicles} vehicles, {Jobs} service jobs.",
            await context.Customers.CountAsync(),
            await context.Vehicles.CountAsync(),
            await context.ServiceJobs.CountAsync());
    }

    /// <summary>
    /// Adds mechanics beyond the single default account so the job board, the employee
    /// directory and the mechanic workload report all have more than one row to show.
    /// </summary>
    private static async Task<List<User>> SeedMechanicsAsync(
        GarijDbContext context, UserManager<IdentityUser> userManager, ILogger logger)
    {
        foreach (var (email, name, phone) in DemoMechanics)
        {
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            var identityUser = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(identityUser, "Mechanic@12345");
            if (!result.Succeeded)
            {
                logger.LogWarning("Could not create demo mechanic {Email}: {Errors}",
                    email, string.Join("; ", result.Errors.Select(e => e.Description)));
                continue;
            }

            await userManager.AddToRoleAsync(identityUser, nameof(UserRole.Mechanic));

            context.StaffUsers.Add(new User
            {
                IdentityUserId = identityUser.Id,
                FullName = name,
                Email = email,
                PhoneNumber = phone,
                Role = UserRole.Mechanic,
                CreatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();

        return await context.StaffUsers.Where(u => u.Role == UserRole.Mechanic).ToListAsync();
    }

    /// <summary>
    /// Builds 24 customers owning 36 vehicles between them, so some customers have a second
    /// car and the service-history screens are not uniformly one-to-one.
    /// </summary>
    private static List<Vehicle> SeedCustomersAndVehicles(GarijDbContext context, Random random)
    {
        var customers = new List<Customer>();
        var usedPlates = new HashSet<string>();
        var vehicles = new List<Vehicle>();

        for (var i = 0; i < 24; i++)
        {
            var fullName = $"{FirstNames[i]} {LastNames[random.Next(LastNames.Length)]}";
            customers.Add(new Customer
            {
                FullName = fullName,
                Email = $"{FirstNames[i].ToLowerInvariant()}.{i + 1:D2}@example.com",
                PhoneNumber = $"+88017{random.Next(10000000, 99999999)}",
                Address = Areas[random.Next(Areas.Length)],
                // Registered over the past two years so "customer since" reads plausibly.
                CreatedAt = DateTime.UtcNow.AddDays(-random.Next(30, 730))
            });
        }

        // 24 customers get one vehicle each, then 12 of them get a second.
        for (var i = 0; i < 36; i++)
        {
            var customer = customers[i < 24 ? i : (i - 24) * 2];
            var (make, model) = Models[random.Next(Models.Length)];

            string plate;
            do
            {
                plate = $"{PlatePrefixes[random.Next(PlatePrefixes.Length)]}-{random.Next(1000, 9999)}";
            }
            while (!usedPlates.Add(plate));

            vehicles.Add(new Vehicle
            {
                Customer = customer,
                LicensePlateNumber = plate,
                Make = make,
                Model = model,
                Year = random.Next(2012, 2025),
                Vin = $"BD{random.Next(100000, 999999)}{random.Next(1000, 9999)}",
                Color = Colors[random.Next(Colors.Length)]
            });
        }

        context.Customers.AddRange(customers);
        context.Vehicles.AddRange(vehicles);

        return vehicles;
    }

    private static void SeedServiceJobs(
        GarijDbContext context,
        Random random,
        List<Vehicle> vehicles,
        List<User> mechanics,
        List<ServiceCatalog> catalog,
        List<Part> parts)
    {
        var statuses = StatusPlan
            .SelectMany(plan => Enumerable.Repeat(plan.Status, plan.Count))
            .ToList();

        var jobNumber = 0;
        var invoiceNumber = 0;

        foreach (var status in statuses)
        {
            jobNumber++;
            var vehicle = vehicles[random.Next(vehicles.Count)];

            // Spread over five months so the monthly revenue report has a real trend line
            // rather than one tall bar.
            var createdAt = DateTime.UtcNow.AddDays(-random.Next(1, 150)).AddHours(-random.Next(0, 24));

            var job = new ServiceJob
            {
                Customer = vehicle.Customer,
                Vehicle = vehicle,
                BookingReference = $"GRJ-2026-{jobNumber:D4}",
                JobType = (JobType)random.Next(0, 3),
                Status = status,
                DiagnosticNotes = status == JobStatus.Requested ? null : DiagnosticNotes[random.Next(DiagnosticNotes.Length)],
                CreatedAt = createdAt,
                CompletedAt = status == JobStatus.Completed ? createdAt.AddDays(random.Next(1, 6)) : null
            };

            AssignMechanics(job, mechanics, random, createdAt);
            AddServiceDetails(job, catalog, random);

            // BR-008: a job cannot reach Completed without logged parts, so every Completed
            // job must carry at least one usage line for the dataset to be legal.
            var minimumParts = status == JobStatus.Completed ? 1 : 0;
            if (status is JobStatus.InProgress or JobStatus.Completed)
            {
                AddPartsUsed(job, parts, random, minimumParts);
            }

            if (status == JobStatus.Completed)
            {
                invoiceNumber++;
                AddInvoiceAndPayment(job, random, invoiceNumber);
                AddCompletionNotification(job, random);
            }

            context.ServiceJobs.Add(job);
        }
    }

    /// <summary>Gives every job past intake exactly one Lead mechanic, and roughly a third of them an Assistant too.</summary>
    private static void AssignMechanics(ServiceJob job, List<User> mechanics, Random random, DateTime createdAt)
    {
        if (job.Status == JobStatus.Requested)
        {
            return;
        }

        var lead = mechanics[random.Next(mechanics.Count)];
        job.MechanicAssignments.Add(new MechanicAssignment
        {
            User = lead,
            RoleInJob = RoleInJob.Lead,
            AssignedAt = createdAt.AddHours(random.Next(1, 6))
        });

        if (mechanics.Count > 1 && random.Next(100) < 35)
        {
            User assistant;
            do
            {
                assistant = mechanics[random.Next(mechanics.Count)];
            }
            while (assistant.Id == lead.Id);

            job.MechanicAssignments.Add(new MechanicAssignment
            {
                User = assistant,
                RoleInJob = RoleInJob.Assistant,
                AssignedAt = createdAt.AddHours(random.Next(6, 24))
            });
        }
    }

    private static void AddServiceDetails(ServiceJob job, List<ServiceCatalog> catalog, Random random)
    {
        var lineCount = random.Next(1, 3);
        var chosen = new HashSet<int>();

        for (var i = 0; i < lineCount; i++)
        {
            var service = catalog[random.Next(catalog.Count)];
            if (!chosen.Add(service.Id))
            {
                continue;
            }

            job.JobServiceDetails.Add(new JobServiceDetail
            {
                ServiceCatalog = service,
                Quantity = 1,
                PriceAtBooking = service.BasePrice
            });
        }
    }

    /// <summary>
    /// Logs parts against the job and decrements stock in step, so the seeded inventory
    /// stays consistent with the usage history and never breaches CK_Part_QuantityInStock.
    /// </summary>
    private static void AddPartsUsed(ServiceJob job, List<Part> parts, Random random, int minimumLines)
    {
        var lineCount = Math.Max(minimumLines, random.Next(1, 4));
        var chosen = new HashSet<int>();

        for (var i = 0; i < lineCount; i++)
        {
            var quantity = random.Next(1, 3);

            // Only consider parts that can actually cover the quantity, so the demo never
            // depends on stock the workshop does not have.
            var affordable = parts
                .Where(p => !chosen.Contains(p.Id) && p.QuantityInStock >= quantity)
                .ToList();

            if (affordable.Count == 0)
            {
                continue;
            }

            var part = affordable[random.Next(affordable.Count)];
            chosen.Add(part.Id);

            part.QuantityInStock -= quantity;
            part.RowVersion = Guid.NewGuid();

            job.JobPartsUsed.Add(new JobPartUsed
            {
                Part = part,
                QuantityUsed = quantity,
                PriceAtUsage = part.UnitPrice
            });
        }
    }

    /// <summary>
    /// Mirrors BillingService.CalculateTotals exactly, including rounding the subtotal once
    /// and deriving tax from that rounded figure, so a seeded invoice is indistinguishable
    /// from one the app generated.
    /// </summary>
    private static void AddInvoiceAndPayment(ServiceJob job, Random random, int invoiceNumber)
    {
        var labourTotal = job.JobServiceDetails.Sum(jsd => jsd.PriceAtBooking * jsd.Quantity);
        var partsTotal = job.JobPartsUsed.Sum(jpu => jpu.PriceAtUsage * jpu.QuantityUsed);

        var subTotal = Math.Round(labourTotal + partsTotal, 2, MidpointRounding.AwayFromZero);
        var taxAmount = Math.Round(subTotal * (TaxRatePercent / 100m), 2, MidpointRounding.AwayFromZero);
        var totalAmount = subTotal + taxAmount;

        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-2026-{invoiceNumber:D4}",
            SubTotal = subTotal,
            TaxAmount = taxAmount,
            TotalAmount = totalAmount,
            PaymentStatus = PaymentStatus.Pending,
            IssuedAt = job.CompletedAt ?? job.CreatedAt
        };

        // Roughly 60% settled, 20% part-paid, 20% still outstanding, so the billing screens
        // and the revenue report show all three states.
        var roll = random.Next(100);
        if (roll < 60)
        {
            invoice.PaymentStatus = PaymentStatus.Paid;
            invoice.PaymentTransactions.Add(new PaymentTransaction
            {
                Amount = totalAmount,
                PaymentMethod = (PaymentMethod)random.Next(0, 3),
                TransactionReference = $"TXN-{invoiceNumber:D4}-F",
                PaidAt = invoice.IssuedAt.AddHours(random.Next(1, 48))
            });
        }
        else if (roll < 80)
        {
            var partial = Math.Round(totalAmount / 2, 2, MidpointRounding.AwayFromZero);
            if (partial > 0)
            {
                invoice.PaymentStatus = PaymentStatus.PartiallyPaid;
                invoice.PaymentTransactions.Add(new PaymentTransaction
                {
                    Amount = partial,
                    PaymentMethod = (PaymentMethod)random.Next(0, 3),
                    TransactionReference = $"TXN-{invoiceNumber:D4}-P",
                    PaidAt = invoice.IssuedAt.AddHours(random.Next(1, 48))
                });
            }
        }

        job.Invoice = invoice;
    }

    /// <summary>Matches the message ServiceJobService raises when a job reaches Completed.</summary>
    private static void AddCompletionNotification(ServiceJob job, Random random)
    {
        var createdAt = job.CompletedAt ?? job.CreatedAt;

        // Leave a third of them unanswered so the approval queue and its badge are not empty
        // when the demo opens.
        var roll = random.Next(100);
        var status = roll < 34 ? NotificationStatus.Pending
            : roll < 84 ? NotificationStatus.Approved
            : NotificationStatus.Rejected;

        job.Notifications.Add(new Notification
        {
            Message = $"Job {job.BookingReference} has been completed and is ready for review.",
            Status = status,
            CreatedAt = createdAt,
            RespondedAt = status == NotificationStatus.Pending ? null : createdAt.AddHours(random.Next(1, 24))
        });
    }

    /// <summary>
    /// Pushes three parts to or under their reorder level so the low-stock alert and the
    /// admin dashboard's reorder table have rows to display during the presentation.
    /// </summary>
    private static void DriveSomePartsBelowReorderLevel(List<Part> parts)
    {
        foreach (var part in parts.OrderBy(p => p.PartNumber).Take(3))
        {
            part.QuantityInStock = Math.Max(0, part.ReorderLevel - 1);
            part.RowVersion = Guid.NewGuid();
        }
    }
}
