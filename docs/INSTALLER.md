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
- `artifacts\installer\PharmaPOS-Setup-1.3.1.exe` — installer (if Inno Setup is installed)

## Updating a shop (USB / manual)

1. Build `PharmaPOS-Setup-1.3.1.exe`.
2. Copy it to the shop PC and run it (admin). Same AppId upgrades in place.
3. Shop sales DB and `appsettings` are **kept**. Only program files are replaced.
4. Reopen PharmaPOS — migrations run automatically.

Silent flags (used by in-app update):

```text
/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /DIR="<install folder>"
```

## Pushing an update to selected shops (VPS)

1. Build the installer (`.\scripts\build-installer.ps1`).
2. On the **vendor PC** (store `STORE-001`), open **Settings → Shop updates**.
3. Publish `PharmaPOS-Setup-x.y.z.exe` (SFTP must be set in Preferences; files go to `/var/www/html/bills/updates`).
4. Tick shops → **Send update to selected shops**.
5. Those shops get a prompt the next time PharmaPOS is open.

First time a shop is still on an older build, install once by USB/setup. Later versions can be pushed.

VPS folder + nginx: see `docs/mysql/pos_updates.sql`.

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
