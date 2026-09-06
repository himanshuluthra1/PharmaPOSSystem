using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaPOS.Domain.Entities.Purchases;

namespace PharmaPOS.Persistence.Configurations;

public class PurchaseReturnConfiguration : IEntityTypeConfiguration<PurchaseReturn>
{
    public void Configure(EntityTypeBuilder<PurchaseReturn> b)
    {
        b.Property(x => x.ReturnNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.SupplierReturnReceiptNumber).HasMaxLength(80);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.HasIndex(x => x.ReturnNumber).IsUnique();
        b.HasIndex(x => x.ReturnDate);
        b.HasIndex(x => x.PurchaseId);
        b.HasIndex(x => x.SupplierReturnReceiptNumber);
        b.HasIndex(x => x.ReturnKind);

        b.HasOne(x => x.Purchase).WithMany()
            .HasForeignKey(x => x.PurchaseId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Supplier).WithMany()
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne(i => i.PurchaseReturn!)
            .HasForeignKey(i => i.PurchaseReturnId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ExpirySupplierClaimConfiguration : IEntityTypeConfiguration<ExpirySupplierClaim>
{
    public void Configure(EntityTypeBuilder<ExpirySupplierClaim> b)
    {
        b.Property(x => x.ClaimNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.CreditNoteNumber).HasMaxLength(80);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.HasIndex(x => x.ClaimNumber).IsUnique();
        b.HasIndex(x => x.ClaimDate);
        b.HasIndex(x => new { x.SupplierId, x.Status });

        b.HasOne(x => x.Supplier).WithMany()
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PurchaseReturn).WithMany()
            .HasForeignKey(x => x.PurchaseReturnId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne(i => i.Claim!)
            .HasForeignKey(i => i.ClaimId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ExpirySupplierClaimItemConfiguration : IEntityTypeConfiguration<ExpirySupplierClaimItem>
{
    public void Configure(EntityTypeBuilder<ExpirySupplierClaimItem> b)
    {
        b.Property(x => x.BatchNumber).HasMaxLength(60);
        b.HasOne(x => x.Medicine).WithMany()
            .HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.MedicineBatch).WithMany()
            .HasForeignKey(x => x.MedicineBatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Purchase).WithMany()
            .HasForeignKey(x => x.PurchaseId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.MedicineBatchId);
    }
}

public class PurchaseReturnItemConfiguration : IEntityTypeConfiguration<PurchaseReturnItem>
{
    public void Configure(EntityTypeBuilder<PurchaseReturnItem> b)
    {
        b.Property(x => x.BatchNumber).HasMaxLength(60);
        b.Property(x => x.ReasonRemarks).HasMaxLength(400);

        b.HasOne(x => x.PurchaseItem).WithMany()
            .HasForeignKey(x => x.PurchaseItemId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Medicine).WithMany()
            .HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.MedicineBatch).WithMany()
            .HasForeignKey(x => x.MedicineBatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReturnReason).WithMany()
            .HasForeignKey(x => x.ReturnReasonId).OnDelete(DeleteBehavior.Restrict);
    }
}
