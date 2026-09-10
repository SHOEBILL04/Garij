using Garij.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garij.Infrastructure.Configurations;

public class JobPartUsedConfiguration : IEntityTypeConfiguration<JobPartUsed>
{
    public void Configure(EntityTypeBuilder<JobPartUsed> builder)
    {
        builder.HasKey(jpu => jpu.Id);

        builder.Property(jpu => jpu.PriceAtUsage).HasColumnType("decimal(18,2)");

        // A usage line of zero is meaningless and a negative one would *increase* stock
        // when the decrement is applied, so the database refuses both outright.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_JobPartUsed_QuantityUsed", "\"QuantityUsed\" > 0");
            t.HasCheckConstraint("CK_JobPartUsed_PriceAtUsage", "\"PriceAtUsage\" >= 0");
        });
    }
}
