using Garij.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garij.Infrastructure.Configurations;

public class ProjectPurchaseConfiguration : IEntityTypeConfiguration<ProjectPurchase>
{
    public void Configure(EntityTypeBuilder<ProjectPurchase> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.LicenseKey)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(p => p.LicenseKey)
            .IsUnique();

        builder.Property(p => p.IdentityUserId)
            .HasMaxLength(450);

        builder.HasIndex(p => p.IdentityUserId);

        builder.Property(p => p.BuyerName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(p => p.BuyerEmail)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(p => p.BuyerEmail);

        builder.Property(p => p.WorkshopName)
            .HasMaxLength(200);

        builder.Property(p => p.GarageId)
            .HasMaxLength(100);

        builder.HasIndex(p => p.GarageId);

        builder.Property(p => p.Amount)
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(p => p.PaymentMethod)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.TransactionReference)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Notes)
            .HasMaxLength(1000);
    }
}
