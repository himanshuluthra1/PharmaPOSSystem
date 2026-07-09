using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Accounting;
using PharmaPOS.Domain.Entities.Identity;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.System;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Persistence.Context;
using PharmaPOS.Shared.Constants;

namespace PharmaPOS.Persistence.Seed;

/// <summary>
/// Ensures the database exists and is populated with the baseline data required
/// to run: permissions, roles, a head-office branch, the default admin user,
/// company profile, chart of accounts and a little sample master data.
/// </summary>
public class DbSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public DbSeeder(ApplicationDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await _context.Database.MigrateAsync(ct);

        await SeedPermissionsAsync(ct);
        var branch = await SeedBranchAsync(ct);
        await SeedRolesAndAdminAsync(branch, ct);
        await SeedCompanyProfileAsync(ct);
        await SeedAccountsAsync(ct);
        await SeedSampleMastersAsync(branch, ct);
        await EnsureDefaultSupplierAsync(branch, ct);

        await _context.SaveChangesAsync(ct);
    }

    private async Task SeedPermissionsAsync(CancellationToken ct)
    {
        var defined = new (string Key, string Name, string Module)[]
        {
            (AppConstants.Permissions.DashboardView, "View Dashboard", "Dashboard"),
            (AppConstants.Permissions.SalesManage, "Manage Sales", "Sales"),
            (AppConstants.Permissions.PurchaseManage, "Manage Purchases", "Purchase"),
            (AppConstants.Permissions.InventoryManage, "Manage Inventory", "Inventory"),
            (AppConstants.Permissions.MastersManage, "Manage Masters", "Masters"),
            (AppConstants.Permissions.AccountingManage, "Manage Accounting", "Accounting"),
            (AppConstants.Permissions.ReportsView, "View Reports", "Reports"),
            (AppConstants.Permissions.SettingsManage, "Manage Settings", "Settings"),
            (AppConstants.Permissions.UsersManage, "Manage Users", "Security"),
        };

        var existing = await _context.Permissions.Select(p => p.Key).ToListAsync(ct);
        foreach (var (key, name, module) in defined)
        {
            if (!existing.Contains(key))
                _context.Permissions.Add(new Permission { Key = key, Name = name, Module = module });
        }
        await _context.SaveChangesAsync(ct);
    }

    private async Task<Branch> SeedBranchAsync(CancellationToken ct)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.IsHeadOffice, ct);
        if (branch is not null) return branch;

        branch = new Branch
        {
            Code = "HO",
            Name = "Head Office",
            IsHeadOffice = true,
            Status = EntityStatus.Active
        };
        _context.Branches.Add(branch);
        await _context.SaveChangesAsync(ct);
        return branch;
    }

    private async Task SeedRolesAndAdminAsync(Branch branch, CancellationToken ct)
    {
        var allPermissions = await _context.Permissions.ToListAsync(ct);

        var superAdmin = await _context.Roles.FirstOrDefaultAsync(r => r.Name == AppConstants.Roles.SuperAdmin, ct);
        if (superAdmin is null)
        {
            superAdmin = new Role
            {
                Name = AppConstants.Roles.SuperAdmin,
                Description = "Full, unrestricted access",
                IsSystemRole = true
            };
            _context.Roles.Add(superAdmin);
            await _context.SaveChangesAsync(ct);

            foreach (var p in allPermissions)
                _context.RolePermissions.Add(new RolePermission { RoleId = superAdmin.Id, PermissionId = p.Id });
            await _context.SaveChangesAsync(ct);
        }

        // Additional out-of-the-box roles.
        foreach (var roleName in new[] { AppConstants.Roles.Admin, AppConstants.Roles.Manager,
                     AppConstants.Roles.Pharmacist, AppConstants.Roles.Cashier, AppConstants.Roles.Accountant })
        {
            if (!await _context.Roles.AnyAsync(r => r.Name == roleName, ct))
                _context.Roles.Add(new Role { Name = roleName, IsSystemRole = true });
        }
        await _context.SaveChangesAsync(ct);

        if (!await _context.Users.AnyAsync(u => u.Username == "admin", ct))
        {
            _context.Users.Add(new User
            {
                Username = "admin",
                PasswordHash = _passwordHasher.Hash("Admin@123"),
                FullName = "System Administrator",
                RoleId = superAdmin.Id,
                BranchId = branch.Id,
                Status = EntityStatus.Active,
                MustChangePassword = true
            });
            await _context.SaveChangesAsync(ct);
        }
    }

    private async Task SeedCompanyProfileAsync(CancellationToken ct)
    {
        if (await _context.CompanyProfiles.AnyAsync(ct)) return;
        _context.CompanyProfiles.Add(new CompanyProfile
        {
            CompanyName = "PharmaPOS Medical Store",
            City = "Mumbai",
            State = "Maharashtra",
            Currency = "INR",
            CurrencySymbol = "\u20B9",
            InvoiceFooter = "Thank you for your purchase. Get well soon!"
        });
        await _context.SaveChangesAsync(ct);
    }

    private async Task SeedAccountsAsync(CancellationToken ct)
    {
        if (await _context.Accounts.AnyAsync(ct)) return;
        var accounts = new[]
        {
            new Account { Code = "1000", Name = "Cash In Hand", Type = AccountType.Asset, IsSystemAccount = true },
            new Account { Code = "1010", Name = "Bank Account", Type = AccountType.Asset, IsSystemAccount = true },
            new Account { Code = "1200", Name = "Accounts Receivable", Type = AccountType.Asset, IsSystemAccount = true },
            new Account { Code = "1300", Name = "Inventory", Type = AccountType.Asset, IsSystemAccount = true },
            new Account { Code = "2000", Name = "Accounts Payable", Type = AccountType.Liability, IsSystemAccount = true },
            new Account { Code = "2100", Name = "GST Payable", Type = AccountType.Liability, IsSystemAccount = true },
            new Account { Code = "4000", Name = "Sales Income", Type = AccountType.Income, IsSystemAccount = true },
            new Account { Code = "5000", Name = "Purchase / COGS", Type = AccountType.Expense, IsSystemAccount = true },
        };
        _context.Accounts.AddRange(accounts);
        await _context.SaveChangesAsync(ct);
    }

    private async Task SeedSampleMastersAsync(Branch branch, CancellationToken ct)
    {
        if (await _context.Medicines.AnyAsync(ct)) return;

        var category = new MedicineCategory { Name = "General" };
        var manufacturer = new Manufacturer { Name = "Generic Pharma Ltd." };
        _context.MedicineCategories.Add(category);
        _context.Manufacturers.Add(manufacturer);
        await _context.SaveChangesAsync(ct);

        var paracetamol = new Medicine
        {
            Name = "Paracetamol 500mg", GenericName = "Paracetamol", Strength = "500mg",
            DosageForm = DosageForm.Tablet, CategoryId = category.Id, ManufacturerId = manufacturer.Id,
            HsnCode = "3004", GstPercent = 12m, Mrp = 25m, PurchasePrice = 15m, SellingPrice = 25m,
            UnitsPerPack = 10, ReorderLevel = 50, Barcode = "8901234567890"
        };
        var amoxicillin = new Medicine
        {
            Name = "Amoxicillin 250mg", GenericName = "Amoxicillin", Strength = "250mg",
            DosageForm = DosageForm.Capsule, CategoryId = category.Id, ManufacturerId = manufacturer.Id,
            HsnCode = "3004", GstPercent = 12m, Mrp = 60m, PurchasePrice = 40m, SellingPrice = 60m,
            UnitsPerPack = 10, ReorderLevel = 30, PrescriptionRequired = true,
            ScheduleType = ScheduleDrugType.ScheduleH, Barcode = "8901234567891"
        };
        var cetirizine = new Medicine
        {
            Name = "Cetirizine 10mg", GenericName = "Cetirizine", Strength = "10mg",
            DosageForm = DosageForm.Tablet, CategoryId = category.Id, ManufacturerId = manufacturer.Id,
            HsnCode = "3004", GstPercent = 5m, Mrp = 30m, PurchasePrice = 18m, SellingPrice = 30m,
            UnitsPerPack = 10, ReorderLevel = 40, Barcode = "8901234567892"
        };
        _context.Medicines.AddRange(paracetamol, amoxicillin, cetirizine);
        await _context.SaveChangesAsync(ct);

        // Opening stock batches so the billing screen is usable out of the box.
        var today = DateTime.UtcNow.Date;
        _context.MedicineBatches.AddRange(
            new MedicineBatch
            {
                MedicineId = paracetamol.Id, BranchId = branch.Id, BatchNumber = "PARA-A100",
                ManufacturingDate = today.AddMonths(-6), ExpiryDate = today.AddMonths(18),
                QuantityAvailable = 200, PurchasePrice = 15m, Mrp = 25m, SellingPrice = 25m, GstPercent = 12m
            },
            new MedicineBatch
            {
                MedicineId = paracetamol.Id, BranchId = branch.Id, BatchNumber = "PARA-B250",
                ManufacturingDate = today.AddMonths(-2), ExpiryDate = today.AddMonths(24),
                QuantityAvailable = 150, PurchasePrice = 16m, Mrp = 25m, SellingPrice = 25m, GstPercent = 12m
            },
            new MedicineBatch
            {
                MedicineId = amoxicillin.Id, BranchId = branch.Id, BatchNumber = "AMOX-X50",
                ManufacturingDate = today.AddMonths(-3), ExpiryDate = today.AddMonths(12),
                QuantityAvailable = 100, PurchasePrice = 40m, Mrp = 60m, SellingPrice = 60m, GstPercent = 12m
            },
            new MedicineBatch
            {
                MedicineId = cetirizine.Id, BranchId = branch.Id, BatchNumber = "CETI-77",
                ManufacturingDate = today.AddMonths(-1), ExpiryDate = today.AddMonths(30),
                QuantityAvailable = 300, PurchasePrice = 18m, Mrp = 30m, SellingPrice = 30m, GstPercent = 5m
            });

        // A sample regular customer for credit/loyalty testing.
        _context.Customers.Add(new Customer
        {
            Name = "Rahul Sharma", Type = CustomerType.Regular, Phone = "9876543210",
            BranchId = branch.Id, CreditLimit = 5000m, Status = EntityStatus.Active
        });

        await _context.SaveChangesAsync(ct);
    }

    private async Task EnsureDefaultSupplierAsync(Branch branch, CancellationToken ct)
    {
        if (await _context.Suppliers.AnyAsync(ct)) return;

        _context.Suppliers.Add(new Supplier
        {
            Name = "MediDistributors Pvt Ltd",
            Phone = "9123456780",
            GstNumber = "27AABCM1234F1Z5",
            ContactPerson = "Rajesh Kumar",
            City = "Mumbai",
            State = "Maharashtra",
            BranchId = branch.Id,
            PaymentTermsDays = 30,
            Status = EntityStatus.Active
        });
        await _context.SaveChangesAsync(ct);
    }
}
