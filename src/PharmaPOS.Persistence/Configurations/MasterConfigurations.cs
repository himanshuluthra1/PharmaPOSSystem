using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;

namespace PharmaPOS.Persistence.Configurations;

public class MedicineConfiguration : IEntityTypeConfiguration<Medicine>
{
    public void Configure(EntityTypeBuilder<Medicine> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.GenericName).HasMaxLength(200);
        b.Property(x => x.Barcode).HasMaxLength(64);
        b.Property(x => x.PackInfo).HasMaxLength(100);
        b.Property(x => x.NameSearchKey)
            .HasMaxLength(200)
            .HasComputedColumnSql("REPLACE([Name], N' ', N'')", stored: true);
        b.Property(x => x.GenericNameSearchKey)
            .HasMaxLength(200)
            .HasComputedColumnSql("REPLACE(ISNULL([GenericName], N''), N' ', N'')", stored: true);
        b.Property(x => x.BarcodeSearchKey)
            .HasMaxLength(64)
            .HasComputedColumnSql("REPLACE(ISNULL([Barcode], N''), N' ', N'')", stored: true);
        b.HasIndex(x => x.Name);
        b.HasIndex(x => x.GenericName);
        b.HasIndex(x => x.Barcode);
        b.HasIndex(x => x.NameSearchKey);
        b.HasIndex(x => x.GenericNameSearchKey);
        b.HasIndex(x => x.BarcodeSearchKey);

        b.HasOne(x => x.Category).WithMany(c => c!.Medicines)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Manufacturer).WithMany(m => m!.Medicines)
            .HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class MedicineBatchConfiguration : IEntityTypeConfiguration<MedicineBatch>
{
    public void Configure(EntityTypeBuilder<MedicineBatch> b)
    {
        b.Property(x => x.BatchNumber).HasMaxLength(60).IsRequired();
        b.HasIndex(x => new { x.MedicineId, x.BatchNumber, x.BranchId });
        b.HasIndex(x => x.ExpiryDate);
        b.HasOne(x => x.Medicine).WithMany(m => m!.Batches)
            .HasForeignKey(x => x.MedicineId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.NameSearchKey)
            .HasMaxLength(200)
            .HasComputedColumnSql("REPLACE([Name], N' ', N'')", stored: true);
        b.Property(x => x.PhoneSearchKey)
            .HasMaxLength(64)
            .HasComputedColumnSql("CAST(REPLACE(ISNULL([Phone], N''), N' ', N'') AS nvarchar(64))", stored: true);
        b.HasIndex(x => x.Name);
        b.HasIndex(x => x.NameSearchKey);
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Name);
        b.HasIndex(x => x.Phone);
    }
}

public class ManufacturerConfiguration : IEntityTypeConfiguration<Manufacturer>
{
    public void Configure(EntityTypeBuilder<Manufacturer> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Name);
    }
}

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Name);
    }
}

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Code).HasMaxLength(30);
    }
}

public class MedicineCategoryConfiguration : IEntityTypeConfiguration<MedicineCategory>
{
    public void Configure(EntityTypeBuilder<MedicineCategory> b)
    {
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.Name);
    }
}

public class MedicineMedWinMappingConfiguration : IEntityTypeConfiguration<MedicineMedWinMapping>
{
    public void Configure(EntityTypeBuilder<MedicineMedWinMapping> b)
    {
        b.Property(x => x.MedWinMedicineName).HasMaxLength(200).IsRequired();
        b.Property(x => x.OneMgMedicineName).HasMaxLength(200).IsRequired();
        b.Property(x => x.OneMgCatalogId).HasMaxLength(50);

        b.HasIndex(x => x.MedWinId).IsUnique();
        b.HasIndex(x => x.OneMgMedicineId);
        b.HasIndex(x => x.MedWinMedicineName);

        b.HasOne(x => x.OneMgMedicine)
            .WithMany()
            .HasForeignKey(x => x.OneMgMedicineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
