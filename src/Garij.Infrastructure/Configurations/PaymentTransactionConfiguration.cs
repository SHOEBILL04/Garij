using Garij.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Garij.Infrastructure.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.HasKey(pt => pt.Id);

        builder.Property(pt => pt.Amount).HasColumnType("decimal(18,2)");
        builder.Property(pt => pt.TransactionReference).HasMaxLength(100);

        // Computed from RefundedAt; not a column.
        builder.Ignore(pt => pt.IsRefunded);

        // A refund stamps RefundedAt on the payment it reverses rather than adding a negative
        // payment row, so a payment row is always a positive amount of money received.
        builder.ToTable(t => t.HasCheckConstraint("CK_PaymentTransaction_Amount", "\"Amount\" > 0"));
    }
}
