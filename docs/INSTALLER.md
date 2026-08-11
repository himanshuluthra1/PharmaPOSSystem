# Building a customer installer

## What customers get in the database

| Included (masters) | Excluded (shop-specific) |
|---|---|
| Medicines (+ salts via GenericName) | Customers / patients |
| Manufacturers, categories | Doctors, employees |
| Suppliers (balances = 0) | Stock batches / quantities |
| MedWin mappings | Sales, purchases, returns |
| Roles, admin, COA, return reasons | Journals / receivables history |

## Prerequisites (developer machine)

1. **SQL Server LocalDB** with your working `PharmaPosDb` (source of master catalogue).
2. **.NET 9 SDK**
3. **Inno Setup 6** — https://jrsoftware.org/isinfo.php (optional for zip-only distribute)

## Build

```powershell
# Full pipeline: DistPrepare → publish → Inno Setup
.\scripts\build-installer.ps1

# If you already built the .bak once:
.\scripts\build-installer.ps1 -SkipDistPrepare
```

Outputs:

- `artifacts\dist\PharmaPosDb_Master.bak` — masters-only backup
- `artifacts\publish\win-x64\` — self-contained app (+ `Data\` backup)
- `artifacts\installer\PharmaPOS-Setup-1.1.0.exe` — installer (if Inno Setup is installed)

## DistPrepare only

```powershell
dotnet run --project tools\PharmaPOS.DistPrepare -- `
  "Server=(localdb)\MSSQLLocalDB;Database=PharmaPosDb;Trusted_Connection=True;TrustServerCertificate=True" `
  artifacts\dist
```

## Customer first launch

1. Install LocalDB if prompted.
2. Launch PharmaPOS → restores `Data\PharmaPosDb_Master.bak` into LocalDB `PharmaPosDb`.
3. Login: `admin` / `Admin@123`

Database files live under `%LocalAppData%\PharmaPOS\Data\`.
