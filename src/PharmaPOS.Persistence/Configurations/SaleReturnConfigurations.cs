using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Entities.System;

namespace PharmaPOS.Persistence.Configurations;

public class SaleReturnConfiguration : IEntityTypeConfiguration<SaleReturn>
{
    public void Configure(EntityTypeBuilder<SaleReturn> b)
    {
        b.Property(x => x.ReturnNumber).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.ReturnNumber).IsUnique();
        b.HasIndex(x => x.ReturnDate);
        b.HasIndex(x => x.SaleId);

        b.HasOne(x => x.Sale).WithMany()
            .HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Items).WithOne(i => i.SaleReturn!)
            .HasForeignKey(i => i.SaleReturnId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Refunds).WithOne(r => r.SaleReturn!)
            .HasForeignKey(r => r.SaleReturnId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.CreditNote).WithOne(c => c.SaleReturn!)
            .HasForeignKey<CreditNote>(c => c.SaleReturnId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SaleReturnItemConfiguration : IEntityTypeConfiguration<SaleReturnItem>
{
    public void Configure(EntityTypeBuilder<SaleReturnItem> b)
    {
        b.HasOne(x => x.SaleItem).WithMany()
            .HasForeignKey(x => x.SaleItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Medicine).WithMany()
            .HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.MedicineBatch).WithMany()
            .HasForeignKey(x => x.MedicineBatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReturnReason).WithMany()
            .HasForeignKey(x => x.ReturnReasonId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ReturnReasonConfiguration : IEntityTypeConfiguration<ReturnReason>
{
    public void Configure(EntityTypeBuilder<ReturnReason> b)
    {
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> b)
    {
        b.Property(x => x.CreditNoteNumber).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.CreditNoteNumber).IsUnique();
        b.HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class NonSaleableStockConfiguration : IEntityTypeConfiguration<NonSaleableStock>
{
    public void Configure(EntityTypeBuilder<NonSaleableStock> b)
    {
        b.HasOne(x => x.Medicine).WithMany()
            .HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.MedicineBatch).WithMany()
            .HasForeignKey(x => x.MedicineBatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.SaleReturnItemId);
    }
}

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.OccurredAtUtc);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}
