using Garij.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garij.Infrastructure.Configurations;

public class ServiceJobConfiguration : IEntityTypeConfiguration<ServiceJob>
{
    public void Configure(EntityTypeBuilder<ServiceJob> builder)
    {
        builder.HasKey(sj => sj.Id);

        builder.Property(sj => sj.BookingReference).IsRequired().HasMaxLength(20);
        builder.HasIndex(sj => sj.BookingReference).IsUnique();

        builder.Property(sj => sj.DiagnosticNotes).HasMaxLength(2000);
        builder.Property(sj => sj.GarageId).HasMaxLength(100);
        builder.HasIndex(sj => sj.GarageId);

        builder.HasOne(sj => sj.Customer)
            .WithMany(c => c.ServiceJobs)
            .HasForeignKey(sj => sj.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(sj => sj.JobServiceDetails)
            .WithOne(jsd => jsd.ServiceJob)
            .HasForeignKey(jsd => jsd.ServiceJobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(sj => sj.MechanicAssignments)
            .WithOne(ma => ma.ServiceJob)
            .HasForeignKey(ma => ma.ServiceJobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(sj => sj.JobPartsUsed)
            .WithOne(jpu => jpu.ServiceJob)
            .HasForeignKey(jpu => jpu.ServiceJobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(sj => sj.Notifications)
            .WithOne(n => n.ServiceJob)
            .HasForeignKey(n => n.ServiceJobId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(sj => sj.Invoice)
            .WithOne(i => i.ServiceJob)
            .HasForeignKey<Invoice>(i => i.ServiceJobId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
