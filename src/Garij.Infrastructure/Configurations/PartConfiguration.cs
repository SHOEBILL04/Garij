using Garij.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garij.Infrastructure.Configurations;

public class PartConfiguration : IEntityTypeConfiguration<Part>
{
    public void Configure(EntityTypeBuilder<Part> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.PartNumber).IsRequired().HasMaxLength(50);
        builder.Property(p => p.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(p => p.GarageId).HasMaxLength(100);
        builder.HasIndex(p => p.GarageId);

        // Included in the WHERE clause of every UPDATE, so a stale writer affects 0 rows
        // and EF Core raises DbUpdateConcurrencyException instead of losing the update.
        builder.Property(p => p.RowVersion).IsConcurrencyToken();

        // Second line of defence behind the service-layer checks: even a direct SQL write
        // or a code path that forgets to validate cannot leave the inventory in a state
        // the domain considers impossible.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Part_QuantityInStock", "\"QuantityInStock\" >= 0");
            t.HasCheckConstraint("CK_Part_UnitPrice", "\"UnitPrice\" >= 0");
            t.HasCheckConstraint("CK_Part_ReorderLevel", "\"ReorderLevel\" >= 0");
        });

        builder.HasMany(p => p.JobPartsUsed)
            .WithOne(jpu => jpu.Part)
            .HasForeignKey(jpu => jpu.PartId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
