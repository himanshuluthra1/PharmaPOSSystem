# MedWin → PharmaPOS Import Guide

This document captures everything needed to migrate a MedWin (Access `.mdb`) pharmacy database into PharmaPOS. Keep it updated when import behaviour changes.

## Quick start

### Fresh start (masters + MedWin transactions)

Recommended path when you want to drop existing POS sales/purchases and live only on MedWin history:

1. **Settings → MedWin Import**
2. Leave **Clear existing transactional data before import** checked (default)
3. Keep **Full import** phases selected (includes stock, purchases, sales, payments)
4. Click **Run import** and accept **both** confirmation dialogs
5. After import: **Medicine Mapping** for unmatched meds

What the wipe removes: sales/returns/payments, purchases/returns/POs, stock batches & movements, journal entries, sync outbox.  
What it keeps: medicines, suppliers, customers, categories, manufacturers, users, roles, company, branches, chart of accounts.

CLI: `--clear-transactions` (runs wipe before selected phases).

### In the app
**Settings → MedWin Import**
1. Browse to MedWin `data.mdb` (typically `D:\Medwin\datafolder\data.mdb`)
2. Enter Jet OLEDB password (default used by many installs: `z111111111111111111a`)
3. Choose **Full import** or individual phases
4. Optionally enable clear-before-import (see above)
5. Click **Run import**
6. After medicines land, use **Settings → Medicine Mapping** to link MedWin orphans to OneMG catalogue (Gemini optional)

Requires **Microsoft ACE OLEDB 12.0** on the PC.

### CLI
```bash
dotnet run --project tools/PharmaPOS.MedWinImport -- --mdb "D:\Medwin\datafolder\data.mdb" --password "z111111111111111111a" --clear-transactions
```

Options: `--phase <name>` (repeatable), `--force`, `--clear-transactions`, `--report-csv <path>`, `--target <sql-conn>`.

---

## Architecture

| Piece | Path |
|-------|------|
| Importer library / CLI | `tools/PharmaPOS.MedWinImport/` |
| In-app UI | Settings → MedWin Import (`MedWinImportTabViewModel`) |
| Post-import mapping | Settings → Medicine Mapping |
| Schema probe tool | `tools/MedWinSchemaProbe/` |

Identity link for MedWin medicines: `Medicines.Notes` contains `MedWinId:{numbercd}`. Later links also use `MedicineMedWinMappings`.

---

## Critical field mappings (do not regress)

### Salt / composition (IMPORTANT)
| MedWin | Meaning | PharmaPOS |
|--------|---------|-----------|
| `mednmas.mgamma` | Salt **group code** only (e.g. `ATO`, `MIN`, `1AL`) — **not** the real salt | Do **not** use as `GenericName` |
| `mednmas.medgrp` | Salt composition code | Join key |
| `itemgrp.itemgrcd` | Same code as `medgrp` (often space-padded) | Join key |
| `itemgrp.itemgrds` | **Real salt / formula text** (e.g. `LEVOCETIRIZINE`, `ATORVASTATIN+EZETIMIBE`) | `GenericName` + `Composition` |

Importer rule:
```text
genericName = itemgrds if present, else fallback to mgamma
```

Access join usually works with padded `medgrp` / `itemgrcd`. When joining in tools, prefer trimming both sides or load `itemgrp` into a dictionary keyed by trimmed code.

Some MedWin groups (e.g. `HIM`) have empty `itemgrds` — those stay as short codes / OTC placeholders.

### Pack / size / strength / price
| MedWin | PharmaPOS |
|--------|-----------|
| `mednmas.medsize` (e.g. `10'S`) | `PackInfo` |
| `mednmas.sizefact` | `UnitsPerPack` |
| `mednmas.mstrngth` | `Strength` |
| `mednmas.mrprate` | `Mrp` |
| `mednmas.fpurrat` / `purrate` | `PurchasePrice` |
| Stock `stkinvrate`, then `wrate` / sale history | `SellingPrice` |
| `mednmas.mcomp` → `compmas` | Manufacturer (`Brand` currently stores company code) |

### Medicine filter
Only medicines that appear in **`stockmas` OR `dsalemaster`** (in stock or ever sold) are imported.

### OneMG matching
Active MedWin medicines are matched to existing OneMG catalogue rows (normalized name / barcode). Matches reuse the OneMG row and append `MedWinId:` to notes. Unmatched MedWin rows are inserted as orphans for Medicine Mapping.

---

## Phase catalogue

| Phase | MedWin source | PharmaPOS target |
|-------|---------------|------------------|
| `company` | `compprof` | `CompanyProfiles` |
| `gst` | `category` | `MedicineCategories` (+ GST on meds) |
| `medicines` | `mednmas` + `itemgrp` + `compmas` + `category` | `Medicines`, `Manufacturers` |
| `suppliers` | `subgroup` (`subgrpty='SC'`) | `Suppliers` |
| `customers` | `subgroup` (`SD`), `patient_master`, cash sale names | `Customers` |
| `stock` | `stockmas` (qty > 0) | `MedicineBatches` + current-stock `StockMovements` snapshot |
| `purchases` | `purchase` / `dpurchas` | `Purchases` / `PurchaseItems` (`MW-P-{n}`) + `StockMovements` (units) |
| `purchase-returns` | `purchase_return` / `dpurchas_return` | `PurchaseReturns` / `PurchaseReturnItems` (`MW-PR-{n}`) + `StockMovements` |
| `sales` | `salemaster` / `dsalemaster` | `Sales` / `SaleItems` (`MW-S-{n}`) + `StockMovements` (**units**, not packs) |
| `payments` | `dsale_payment`, `dsale_receipt` | `SalePayments` |
| `users` | distinct `oprcodeadd` | `Users` (`op{n}`, default password `MedWin@123`) |
| `backfill-expiry` | MedWin month/year fields | Sale lines / batches expiry |
| `backfill-purchase-payments` | header `pcredit` / `pcheqamt` | `PaidAmount` / payment status |
| `backfill-purchase-tax` | Sum of `PurchaseItems` tax/taxable | Fix header `Cgst`/`Sgst`/`Taxable` (MedWin `purtaxam` was often taxable, not tax) |
| `backfill-salts` | Re-resolve `itemgrp.itemgrds` onto existing MedWin orphans | `GenericName`, `Composition`, `PackInfo`, `Strength` |
| `dedupe-onemg` | — | Soft-delete duplicate OneMG catalogue rows |

Default **Full import** runs: company, gst, medicines, suppliers, customers, stock, purchases, purchase-returns, sales, payments, users.

### Quantity units (important)
MedWin `stockmas`, purchase lines, purchase returns, and item ledger use **loose units** (e.g. tablets).  
Sales `dsalemaster.dpqty` is also in units (`dpsize` is pack size only).  
PharmaPOS import keeps **the same unit basis** everywhere (do not divide sales by `dpsize`). Sale MRP/rate are normalized to per-unit when `dpsize > 1`. Negative sale qty = sale return.

Optional pre-phase: `clear-transactions` (via UI checkbox / `--clear-transactions`) — hard-deletes existing POS transactional & stock data first.

---

## Known gaps (not imported yet)

- Standalone sale-return masters (`salereturnmaster` / `dsalemaster_return` — usually empty; returns often sit on sale bills as negative qty)
- Purchase receipts ledger (`purrcpt`) beyond header paid fields
- Doctors master, schemes, loyalty points
- Accounting ledgers (`daccount`)
- Inactive / never-sold medicines
- Full customer credit/outstanding mirror

---

## Connection defaults

| Setting | Default |
|---------|---------|
| MDB path | `D:\Medwin\datafolder\data.mdb` |
| Jet password | `z111111111111111111a` |
| Target SQL | LocalDB `PharmaPosDb` from app `ConnectionStrings:PharmaPosDb` |

Provider: `Microsoft.ACE.OLEDB.12.0`.

---

## Operational tips

1. **Backup** PharmaPosDb before `--force` or full re-import.
2. Run medicines first; then Medicine Mapping for orphans.
3. Use `--report-csv` to preview OneMG matches without writing.
4. After salt logic changes, run phase `backfill-salts` on existing orphans.
5. Invoice numbers from MedWin are prefixed `MW-S-` / `MW-P-` so they do not collide with POS numbering.
6. If OLEDB open fails, install Access Database Engine (ACE) x64 matching the app bitness.

---

## Related code entry points

- `MedWinMigrationRunner.RunAsync` — public facade (CLI + WPF Settings)
- `MedWinTransactionalDataCleaner` — wipe sales/purchases/stock before fresh MedWin import
- `MedWinImporter.RunAsync` — phase orchestrator
- `MedWinMasterImporter.ImportMedicinesAsync` — salt/pack/medicine insert + OneMG match
- `MedWinMasterImporter.BackfillSaltsAsync` — repair salts on existing orphans
- `MedWinTransactionImporter` — sales / purchases / payments / backfills
- `MedicineCatalogMatcher` — OneMG catalogue matching
- `MedicineNotesHelper` — parse `MedWinId:` / `OneMG-ID:` from Notes
- `MedWinImportTabViewModel` — Settings → MedWin Import UI
