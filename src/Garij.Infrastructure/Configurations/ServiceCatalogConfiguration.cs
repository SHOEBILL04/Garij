using Garij.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garij.Infrastructure.Configurations;

public class ServiceCatalogConfiguration : IEntityTypeConfiguration<ServiceCatalog>
{
    public void Configure(EntityTypeBuilder<ServiceCatalog> builder)
    {
        builder.HasKey(sc => sc.Id);

        builder.Property(sc => sc.Name).IsRequired().HasMaxLength(200);
        builder.Property(sc => sc.Description).HasMaxLength(1000);
        builder.Property(sc => sc.BasePrice).HasColumnType("decimal(18,2)");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ServiceCatalog_BasePrice", "\"BasePrice\" >= 0");
            t.HasCheckConstraint("CK_ServiceCatalog_EstimatedDurationMinutes", "\"EstimatedDurationMinutes\" > 0");
        });

        builder.HasMany(sc => sc.JobServiceDetails)
            .WithOne(jsd => jsd.ServiceCatalog)
            .HasForeignKey(jsd => jsd.ServiceCatalogId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
