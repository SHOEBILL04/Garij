using Garij.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garij.Infrastructure.Configurations;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.HasKey(v => v.Id);

        builder.Property(v => v.LicensePlateNumber).IsRequired().HasMaxLength(20);
        builder.HasIndex(v => v.LicensePlateNumber).IsUnique();

        builder.Property(v => v.Make).IsRequired().HasMaxLength(100);
        builder.Property(v => v.Model).IsRequired().HasMaxLength(100);
        builder.Property(v => v.Vin).HasMaxLength(50);
        builder.Property(v => v.Color).HasMaxLength(50);

        // Catches an unset (0) or typo'd model year before it reaches a service history report.
        builder.ToTable(t => t.HasCheckConstraint("CK_Vehicle_Year", "\"Year\" BETWEEN 1900 AND 2100"));

        builder.HasMany(v => v.ServiceJobs)
            .WithOne(sj => sj.Vehicle)
            .HasForeignKey(sj => sj.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
