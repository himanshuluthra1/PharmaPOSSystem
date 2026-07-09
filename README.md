# PharmaPOS — Pharmacy & Medical Store POS

A professional, modular Pharmacy / Medical Store Point-of-Sale desktop application
built on **.NET 9 + WPF (MVVM)** with **Material Design**, **Clean Architecture**,
**Entity Framework Core** and **SQL Server**.

This repository is being built in phases toward a production-grade ERP-class product
(comparable to Marg ERP, GoFrugal, RetailGraph). **Phase 1 (foundation) is complete
and runnable.** Subsequent phases layer the full transactional modules on top of the
architecture established here.

---

## What works today (Phase 1)

- ✅ Clean Architecture solution (Domain / Application / Infrastructure / Persistence / Shared / WPF / Tests)
- ✅ Comprehensive normalized domain model + EF Core schema (masters, sales, purchase, inventory, batches, accounting, security)
- ✅ Initial database migration + automatic seeding (roles, permissions, admin user, chart of accounts, sample medicines)
- ✅ Role-based authentication (BCrypt hashing, lockout, login history)
- ✅ Premium Material Design UI: branded **Login**, app shell with **navigation rail**, **dark/light theme** toggle
- ✅ Live **Dashboard** with KPI cards (sales, purchase, receivables/payables, low-stock, expiry) and charts
- ✅ Scaffolded, navigable module screens (Sales, Purchase, Inventory, Masters, Accounting, Reports, Settings)
- ✅ Unit tests (security, results) — all green

---

## Tech stack

| Concern            | Technology                                              |
|--------------------|---------------------------------------------------------|
| Language / Runtime | C# 13, .NET 9                                            |
| Desktop UI         | WPF, MVVM, MaterialDesignInXAML 5                        |
| Data               | Entity Framework Core 9, SQL Server / LocalDB           |
| Auth               | Role-based, BCrypt password hashing                     |
| DI / Config        | Microsoft.Extensions.Hosting / DependencyInjection      |
| Tests              | xUnit                                                    |

---

## Prerequisites

- **.NET 9 SDK**
- **SQL Server** — LocalDB (ships with Visual Studio) or a full instance.
  The default connection string uses `(localdb)\MSSQLLocalDB`.

---

## Getting started

```bash
# 1. Restore & build
dotnet build

# 2. (Optional) point at your own SQL Server by editing:
#    src/PharmaPOS.WPF/appsettings.json  ->  ConnectionStrings:PharmaPosDb

# 3. Run the desktop app (creates + seeds the DB on first launch)
dotnet run --project src/PharmaPOS.WPF
```

**Default login:** `admin` / `Admin@123` (you'll be prompted to change it later).

### Database migrations

The app auto-applies migrations at startup. To manage them manually:

```bash
# add a migration
dotnet ef migrations add <Name> --project src/PharmaPOS.Persistence

# apply to the database
dotnet ef database update --project src/PharmaPOS.Persistence
```

### Run tests

```bash
dotnet test
```

---

## Solution structure (Clean Architecture)

```
PharmaPOS.sln
├── src/
│   ├── PharmaPOS.Domain          # Entities, enums, domain rules (no dependencies)
│   ├── PharmaPOS.Application      # Use-cases, DTOs, service & repository abstractions
│   ├── PharmaPOS.Infrastructure   # Cross-cutting: hashing, clock, current-user
│   ├── PharmaPOS.Persistence      # EF Core DbContext, configs, repositories, migrations, seeding
│   ├── PharmaPOS.Shared           # Constants, Result type (shared kernel)
│   └── PharmaPOS.WPF              # Presentation: MVVM views, view models, DI host
└── tests/
    └── PharmaPOS.UnitTests
```

Dependencies flow **inward**: `WPF → Application/Infrastructure/Persistence → Domain → Shared`.
The Application layer defines abstractions (`IRepository<T>`, `IUnitOfWork`, `IPasswordHasher`,
`ICurrentUserService`, `IDateTimeProvider`) that outer layers implement — keeping the
core testable and framework-independent.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md), [`docs/DATABASE.md`](docs/DATABASE.md)
and [`docs/ROADMAP.md`](docs/ROADMAP.md) for details.

---

## Phased delivery plan

| Phase | Scope                                                        | Status         |
|-------|-------------------------------------------------------------|----------------|
| 1     | Architecture, schema, auth, dashboard, UI shell             | ✅ Complete    |
| 2     | Master data CRUD, user/role management, settings            | ⏳ Next        |
| 3     | Purchase, Sales (fast billing), Inventory                   | ⏳ Planned     |
| 4     | Accounting, Reports, Barcode, Printing                      | ⏳ Planned     |
| 5     | Multi-branch, Backup, Security hardening, Notifications      | ⏳ Planned     |
| 6     | AI features, performance, installer, full test coverage     | ⏳ Planned     |

---

## Keyboard-first design (planned for billing)

The billing screen is being designed around fast keyboard operation:
`F2` Customer · `F3` Search medicine · `F4` Discount · `F5` Hold · `F6` Resume ·
`F7` Payment · `F8` Print · `F9` Save · `Esc` Cancel.

---

## License

Proprietary — all rights reserved (adjust as appropriate for your use).
