using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;

namespace PharmaPOS.Persistence.Configurations;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.Property(x => x.InvoiceNumber).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.InvoiceNumber).IsUnique();
        b.HasIndex(x => x.InvoiceDate);
        b.Property(x => x.LockedBy).HasMaxLength(100);

        b.HasMany(x => x.Items).WithOne(i => i.Sale!)
            .HasForeignKey(i => i.SaleId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Payments).WithOne(p => p.Sale!)
            .HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Doctor).WithMany()
            .HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Counter).WithMany()
            .HasForeignKey(x => x.CounterId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CounterSession).WithMany()
            .HasForeignKey(x => x.CounterSessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CounterId);
        b.HasIndex(x => x.CounterSessionId);
    }
}

public class BillingCounterConfiguration : IEntityTypeConfiguration<BillingCounter>
{
    public void Configure(EntityTypeBuilder<BillingCounter> b)
    {
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        b.HasMany(x => x.Sessions).WithOne(s => s.Counter!)
            .HasForeignKey(s => s.CounterId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class CounterSessionConfiguration : IEntityTypeConfiguration<CounterSession>
{
    public void Configure(EntityTypeBuilder<CounterSession> b)
    {
        b.Property(x => x.MachineName).HasMaxLength(100);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CounterId, x.Status });
        b.HasIndex(x => new { x.UserId, x.Status });
    }
}

public class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> b)
    {
        b.HasOne(x => x.Medicine).WithMany()
            .HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.MedicineBatch).WithMany()
            .HasForeignKey(x => x.MedicineBatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> b)
    {
        b.Property(x => x.InvoiceNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.PartialPaymentNotes).HasMaxLength(400);
        b.Property(x => x.LockedBy).HasMaxLength(100);
        b.HasIndex(x => x.InvoiceNumber).IsUnique();
        b.HasIndex(x => x.InvoiceDate);
        b.HasIndex(x => x.LinkedPurchaseReturnId);

        b.HasMany(x => x.Items).WithOne(i => i.Purchase!)
            .HasForeignKey(i => i.PurchaseId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Supplier).WithMany()
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PurchaseOrder).WithMany()
            .HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LinkedPurchaseReturn).WithMany()
            .HasForeignKey(x => x.LinkedPurchaseReturnId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseItemConfiguration : IEntityTypeConfiguration<PurchaseItem>
{
    public void Configure(EntityTypeBuilder<PurchaseItem> b)
    {
        b.HasOne(x => x.Medicine).WithMany()
            .HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.MedicineBatch).WithMany()
            .HasForeignKey(x => x.MedicineBatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.OrderNumber).IsUnique();
        b.HasMany(x => x.Items).WithOne(i => i.PurchaseOrder!)
            .HasForeignKey(i => i.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Supplier).WithMany()
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}
