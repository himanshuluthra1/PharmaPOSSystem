# Architecture

PharmaPOS follows **Clean Architecture** with strict inward-pointing dependencies,
the **Repository + Unit of Work** patterns, and **MVVM** in the presentation layer.

## Layers

```
┌─────────────────────────────────────────────────────────────┐
│ Presentation (PharmaPOS.WPF)                                  │
│  Views (XAML) · ViewModels · Navigation/Theme/Dialog services │
│  Generic Host + DI composition root                           │
└───────────────▲───────────────────────────────▲──────────────┘
                │                                 │
┌───────────────┴───────────────┐   ┌────────────┴───────────────┐
│ Application                    │   │ Infrastructure / Persistence│
│  Use-cases (AuthService,       │   │  BCryptPasswordHasher        │
│  DashboardService)             │   │  CurrentUserService          │
│  Abstractions:                 │◄──┤  SystemDateTimeProvider      │
│   IRepository<T>, IUnitOfWork, │   │  ApplicationDbContext        │
│   IPasswordHasher,             │   │  Repository<T>, UnitOfWork   │
│   ICurrentUserService,         │   │  Configurations, Migrations  │
│   IDateTimeProvider            │   │  DbSeeder                     │
└───────────────▲────────────────┘   └────────────▲───────────────┘
                │                                   │
        ┌───────┴────────────────────────────────┐ │
        │ Domain (entities, enums, BaseEntity)    │ │
        └───────▲─────────────────────────────────┘ │
                │                                     │
        ┌───────┴─────────────────────────────────┐ │
        │ Shared (Result, constants)  ◄────────────┘ │
        └────────────────────────────────────────────┘
```

- **Domain** — pure entities and enums. `BaseEntity` provides `Id`, audit fields and
  soft-delete metadata; `BranchEntity` adds branch scoping for the multi-branch model.
- **Application** — orchestrates use-cases and owns the interfaces the outer layers
  implement. Depends only on Domain + Shared (plus EF Core abstractions for
  composable `IQueryable` reads).
- **Infrastructure** — cross-cutting implementations (password hashing, clock,
  current-user session).
- **Persistence** — EF Core: `ApplicationDbContext`, entity type configurations,
  generic `Repository<T>`, `UnitOfWork` (with explicit transactions), migrations, seeding.
- **Presentation (WPF)** — MVVM. Views are mapped to view models via `DataTemplate`s in
  `App.xaml`; navigation is data-driven through `INavigationService`.

## Key cross-cutting concerns

- **Auditing** — `ApplicationDbContext.SaveChangesAsync` stamps `CreatedBy/At`,
  `ModifiedBy/At` and converts hard deletes into **soft deletes**.
- **Soft delete** — a global query filter excludes `IsDeleted` rows automatically.
- **Security** — passwords hashed with BCrypt (work factor 12); failed-login lockout;
  login history; role → permission model enforced in the UI and services.
- **Lifetimes** — `DbContext`, `UnitOfWork`, repositories and view models are
  **transient** (one short-lived context per view model), matching the single-user
  desktop grain; `CurrentUserService`, hashing and clock are singletons.

## Presentation composition

`App.xaml.cs` builds a `Host`, registers all layers
(`AddApplication()`, `AddInfrastructure()`, `AddPersistence(config)`), seeds the DB,
then drives the **Login → Shell → Logout** lifecycle. View models are resolved from DI
so their dependencies (services, unit of work) are injected.
