# Delivery Roadmap

The full feature set from the product brief is delivered in phases on top of the
Phase-1 foundation. Each module ships MVVM screens backed by Application-layer
services and repository-based persistence, with unit/integration tests.

## Phase 1 — Foundation ✅ (done)
- Clean Architecture solution & DI composition
- Domain model + normalized EF Core schema + initial migration + seeding
- Role-based authentication (BCrypt, lockout, login history)
- Material Design shell: Login, navigation rail, dark/light theme
- Live Dashboard (KPIs + charts)
- Scaffolded, navigable module screens
- Unit tests

## Phase 2 — Masters, Users & Settings
- CRUD for Medicine, Manufacturer, Supplier, Customer, Doctor, Employee, Category
- Reusable data-grid/search/edit components; bulk import/export
- User & role management, module permissions editor
- Company/store/tax/printer settings, forgot-password & change-password flows
- Session timeout & activity logging UI

## Phase 3 — Purchase, Sales, Inventory
- Purchase orders → GRN with batch/expiry/free-qty/scheme, supplier ledger
- **Fast billing** screen (keyboard-first), barcode & multi-field search
- Batch allocation (FIFO/FEFO), split payments, hold/resume, returns & exchange
- Reward points, coupons; stock ledger, adjustments, transfers, valuation
- Near-expiry/expired/dead-stock, auto-reorder

## Phase 4 — Accounting, Reports, Barcode, Printing
- Cash/bank book, journals, payment/receipt/expense, ledgers
- P&L, Balance Sheet, Trial Balance, GST/TDS reports
- Report engine (RDLC), PDF & Excel export
- Barcode generation/printing/scanning (Zebra, labels, QR)
- Thermal & A4 GST invoice templates, cash drawer, customer display

## Phase 5 — Multi-branch, Backup, Security, Notifications
- Central inventory, branch transfer, consolidated & per-branch reports
- Automatic/manual/cloud backup, scheduler, encrypted backups
- Security hardening (encryption at rest, delete/edit logs, auto-logout)
- Notification center (low stock, expiry, dues, license/GST reminders)

## Phase 6 — AI, Performance, Installer, QA
- AI: demand prediction, sales forecast, OCR invoice reading, drug-interaction &
  duplicate-medicine detection, expiry prediction, recommendations, chatbot
- Performance: caching, background jobs, offline mode, query tuning for 200k+
  medicines / millions of invoices (<100 ms search)
- Windows installer + auto-update
- Full unit/integration test coverage, documentation, XML docs

## Future platforms
Online store, mobile app, doctor/supplier portals, delivery tracking, online payments,
telemedicine, medicine subscriptions.
