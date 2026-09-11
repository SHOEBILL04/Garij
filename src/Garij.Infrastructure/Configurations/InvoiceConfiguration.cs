using Garij.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garij.Infrastructure.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(50);
        builder.Property(i => i.SubTotal).HasColumnType("decimal(18,2)");
        builder.Property(i => i.TaxAmount).HasColumnType("decimal(18,2)");
        builder.Property(i => i.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(i => i.GarageId).HasMaxLength(100);
        builder.HasIndex(i => i.GarageId);

        builder.HasIndex(i => i.ServiceJobId).IsUnique();

        // Backstop for the transactional invoice calculation: a rolled-back or half-written
        // invoice can never be persisted with negative money on it.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Invoice_SubTotal", "\"SubTotal\" >= 0");
            t.HasCheckConstraint("CK_Invoice_TaxAmount", "\"TaxAmount\" >= 0");
            t.HasCheckConstraint("CK_Invoice_TotalAmount", "\"TotalAmount\" >= 0");
        });

        builder.HasMany(i => i.PaymentTransactions)
            .WithOne(pt => pt.Invoice)
            .HasForeignKey(pt => pt.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
