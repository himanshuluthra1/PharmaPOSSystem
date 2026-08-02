using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaPOS.Domain.Entities.Inventory;

namespace PharmaPOS.Persistence.Configurations;

public class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> b)
    {
        b.Property(x => x.TransferNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.FromBranchCode).HasMaxLength(40).IsRequired();
        b.Property(x => x.FromBranchName).HasMaxLength(120).IsRequired();
        b.Property(x => x.ToBranchCode).HasMaxLength(40).IsRequired();
        b.Property(x => x.ToBranchName).HasMaxLength(120).IsRequired();
        b.Property(x => x.PackageKey).HasMaxLength(64).IsRequired();
        b.Property(x => x.ExternalPackageKey).HasMaxLength(64);
        b.Property(x => x.CancelReason).HasMaxLength(500);

        b.HasIndex(x => x.TransferNumber);
        b.HasIndex(x => x.TransferDate);
        b.HasIndex(x => x.ToBranchId);
        b.HasIndex(x => x.PackageKey);
        b.HasIndex(x => x.ExternalPackageKey);

        b.HasOne(x => x.ToBranch).WithMany()
            .HasForeignKey(x => x.ToBranchId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Items).WithOne(i => i.StockTransfer!)
            .HasForeignKey(i => i.StockTransferId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockTransferItemConfiguration : IEntityTypeConfiguration<StockTransferItem>
{
    public void Configure(EntityTypeBuilder<StockTransferItem> b)
    {
        b.Property(x => x.BatchNumber).HasMaxLength(60).IsRequired();
        b.Property(x => x.MedicineName).HasMaxLength(200).IsRequired();
        b.Property(x => x.MedicineBarcode).HasMaxLength(80);
        b.Property(x => x.RackNumber).HasMaxLength(40);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.PurchasePrice).HasPrecision(18, 4);
        b.Property(x => x.Mrp).HasPrecision(18, 4);
        b.Property(x => x.SellingPrice).HasPrecision(18, 4);
        b.Property(x => x.GstPercent).HasPrecision(9, 4);

        b.HasOne(x => x.Medicine).WithMany()
            .HasForeignKey(x => x.MedicineId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SourceMedicineBatch).WithMany()
            .HasForeignKey(x => x.SourceMedicineBatchId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DestinationMedicineBatch).WithMany()
            .HasForeignKey(x => x.DestinationMedicineBatchId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
