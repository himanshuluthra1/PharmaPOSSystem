# PharmaPOS — Detailed User Guide

> **हिंदी गाइड:** [USER_GUIDE_HINDI.md](./USER_GUIDE_HINDI.md) — दुकान कर्मचारियों के लिए सरल, कदम-दर-कदम हिंदी उपयोगकर्ता गाइड।

This guide is for **shop owners, pharmacists, and cashiers**. It explains every screen and everyday job in PharmaPOS: selling, buying, stock, GST, returns, expiry-to-company, printing, backup, and users.

Menus you see depend on your **role**. If a tab is missing, you do not have that permission. Ask an administrator.

---

## Table of contents

1. [What the software is](#1-what-the-software-is)
2. [Install, first login, and store code](#2-install-first-login-and-store-code)
3. [The main window](#3-the-main-window)
4. [Dashboard](#4-dashboard)
5. [Sales (billing)](#5-sales-billing)
6. [Sale returns](#6-sale-returns)
7. [Purchase (goods inward)](#7-purchase-goods-inward)
8. [Purchase orders](#8-purchase-orders)
9. [Purchase returns](#9-purchase-returns)
10. [Expiry to company](#10-expiry-to-company)
11. [Inventory](#11-inventory)
12. [Masters](#12-masters)
13. [Accounting](#13-accounting)
14. [Reports](#14-reports)
15. [Settings](#15-settings)
16. [Printing, paper sizes, and UPI QR](#16-printing-paper-sizes-and-upi-qr)
17. [Keyboard shortcuts](#17-keyboard-shortcuts)
18. [Roles and permissions](#18-roles-and-permissions)
19. [Daily / weekly / monthly routines](#19-daily--weekly--monthly-routines)
20. [Problems and tips](#20-problems-and-tips)

---

## 1. What the software is

PharmaPOS is a **Windows desktop** pharmacy POS. It keeps:

- A **medicine catalogue** (name, salt, GST, barcode, rack/bin, schedule).
- **Stock by batch and expiry** (not just a single quantity per medicine).
- **Sales invoices** with GST, discounts, split payments (cash / UPI / card / credit).
- **Purchases** from suppliers, including free quantity and scanned supplier bills.
- **Returns** (customer and supplier) and **expiry claims** to the company.
- **Accounts**: customer dues, supplier payments, cash book.
- **Reports** for GST, profit, Schedule H/H1, expiry, and stock value.

Stock is always on a **batch**. Selling picks a batch (usually nearest expiry). Purchasing creates or increases that batch.

---

## 2. Install, first login, and store code

### Shop PC install

On a new Windows 10/11 PC, run the shop installer (`Install-PharmaPOS-Shop.bat` or the Setup `.exe`). You need:

- 64-bit Windows and an internet connection for first download.
- **SQL Server Express LocalDB** (free). If the app says LocalDB is missing, install LocalDB and open PharmaPOS again.

Launch **PharmaPOS** from the Start menu.

### First login

Default administrator (change immediately):

| Field | Value |
|--------|--------|
| Username | `admin` |
| Password | `Admin@123` |

You will be asked to **change the password**. Do not leave the default password on a live shop PC.

### Store code

On first run you may be asked for a **store code** from your vendor. That identifies this shop for updates and (if configured) reporting sync. Enter it carefully; it shows in the top bar after login.

### Billing counter

If the shop has more than one billing desk, you may be asked to pick a **counter**. Day-close and cash totals are per counter. You can switch counter later from Sales.

### Database

The database is created and updated **automatically** when the app starts. You do not run SQL scripts for normal use.

---

## 3. The main window

### Top bar

- **PharmaPOS**, **store code**, and **branch** name.
- **Dark / light** theme (moon / sun toggle).
- Signed-in **user name** and **role**.
- **Sign out** (door icon).

Shortcut chips under the bar change with the current module (for example F9 Save on Sales).

### Module menu (second row)

Click a module. Typical items:

| Menu | Who uses it |
|------|-------------|
| Dashboard | Everyone with dashboard access |
| Sales (F2) | Cashiers |
| Purchase | Receiving staff |
| Purchase Order | Ordering |
| Purchase Return | Returns to supplier |
| Expiry to company | Near-expiry / expired return-to-company |
| Inventory | Stock, adjustments, transfers, shortage book |
| Masters | Catalogue and parties |
| Accounting | Dues, vouchers, cash book |
| Reports | Owner / accountant |
| Settings | Admin |

**F2** from almost anywhere opens **Sales**.

---

## 4. Dashboard

Home screen after login (if allowed).

**Today**

- **Today’s sales** and invoice count.
- **Today’s purchase**.
- **Today’s customers** (distinct billed patients).
- **Receivables** (customers who still owe).

**Alerts**

- **Low stock** — at or below reorder level.
- **Near expiry** — batches inside the “near expiry days” setting (default 90).
- **Expired** — expiry date already passed, with stock.
- **Payables** — amounts owed to suppliers.

**Lists**

- **Top selling medicines** (last 30 days) with revenue.
- **Monthly sales** (last 6 months).

Use **Refresh** after a busy period. Numbers are for the signed-in user’s branch.

---

## 5. Sales (billing)

Open **Sales (F2)**. This is the main counter screen.

### Layout

- **Left / centre**: cart grid (items, batch, rack/bin, expiry, qty, MRP, sale price, discount, GST, amount).
- **Right panel**: customer, doctor, payments, totals, Save / Print.
- **Top**: barcode box, camera scan, search bills, sale return, counter, day cash, day close.
- **Bill chips**: several customers at once (`Ctrl+1` … `Ctrl+4`).

### Start a bill

1. Optional: type **patient name** and **mobile** (and address if needed). **F3** / save customer stores them for next time.
2. Optional: **doctor** name (needed for Schedule H/H1 register quality).
3. Add medicines (below).
4. Choose payment(s).
5. **F9** — Save and print (or Update if you opened an existing bill).

### Add a medicine

**Search (most common)**  
Click the **Item** cell (or press **Enter** on an empty line). Type at least **2 characters**. Space-insensitive: `para500` finds `Para 500`.  
Select with Enter. If several batches exist, pick one (nearest expiry is listed first).

**Barcode**  
Focus the barcode box, scan with a USB scanner, press Enter. Or use the **camera** button.

**Create from website**  
If search finds nothing, **Not found? Create from website** can pull details from 1MG / Apollo / Pharmeasy / TrueMeds / NetMeds, then save to masters.

**Substitute (F5)**  
Same salt / similar medicines when the asked brand is out of stock.

**Rack / bin**  
Shown on search, batch picker, and cart so staff can fetch the pack without asking. Set rack and bin on **Masters → Medicines**.

**Medicine details (F4)**  
Popup: stock, cost, MRP, location, packing.

**Medicine ledger (Ctrl+L)**  
Movements for that medicine (purchases, sales, adjustments).

### Quantity, price, discount

- **Quantity** — units sold. Cannot exceed available stock on that batch.
- **MRP / Sale price** — prices are **GST-inclusive** (Indian pharmacy style). GST is extracted from the price, not added on top.
- **Discount %** — line discount (needs discount permission). Changing sale price updates discount and vice versa.
- Remove a line with the row delete button.

### Multiple bills (walk-in queue)

- **Ctrl+T** — new customer bill (new chip).
- **Ctrl+1** to **Ctrl+4** — switch bill.
- **Ctrl+W** — close this customer’s parked bill (does not delete a saved invoice).
- **Esc** — clear the current cart (unsaved).

Each chip shows item count and amount.

### Payments

- Default method is often **Cash**.
- **Add payment** for split tenders (e.g. cash + UPI).
- **Credit** leaves a **balance due** on the customer (collect later in Accounting).
- **Change** appears when cash received exceeds the bill.

### Save, print, share

- **F9** saves the invoice (number like `INV-20260822-0001` — prefix and date from Settings).
- A **preview** opens: choose **paper** (A4 / A5 / 80 mm / 58 mm) then **Print**.
- If WhatsApp / SMS share is enabled in Preferences, you may be asked to share the bill after save.

Saved bills **lock**. To change them, Preferences must allow bill editing, and you (or an admin) must **Unlock**. Super Admin / Full Sales Access can still unlock.

### Refill from last sale (F6)

Type the patient name or mobile, pick a previous bill, choose quantities. Useful for repeat prescriptions.

### Search old bills

Toolbar **search bills** (patient, mobile, or medicine). Open to reprint or (if allowed) edit after unlock.

### Billing counter and day close

- **Switch counter** if this PC is used on more than one desk.
- **Today’s cash by counter** — running cash / UPI / card.
- **Day close** — count drawer cash vs system (opening float + cash sales). Print or save PDF. Do this at shift end.

### Zero stock / shortage book

If a searched medicine has **no stock**, you can add it to the **shortage book** (lost sale). Follow up from Inventory → Shortage book.

### Prescription / Schedule drugs

Medicines marked **Prescription required** or **Schedule H / H1 / X** should have patient and doctor filled for a proper Schedule register. The system can warn based on Settings / return policy; it does not replace a pharmacist’s legal judgement.

---

## 6. Sale returns

Open from Sales (**F8**) or the return screen.

1. **Search** the original bill: invoice number, patient, mobile, medicine, or receipt QR / invoice.
2. Select the sale.
3. Enter **return quantity** per line (cannot exceed originally sold, net of earlier returns).
4. Optional **reason**.
5. Choose **refund** (same mode as original when the shop policy requires it, or cash / credit note).
6. Process. Stock returns to the **same batch**. A credit note may be issued with a validity period (Settings).

**High-value** returns and **policy overrides** (expired, schedule drugs, refrigerated) need extra permissions. Return window in days is set in company / return policy settings.

The return prints as a **credit note / refund** slip.

---

## 7. Purchase (goods inward)

**Purchase** receives a supplier bill and puts stock on the shelf.

### Header

- **Supplier** (required).
- **Supplier invoice number** and **date**.
- **Payment**: paid now, partial, or credit (payables). Partial payment can store a **reason**.

### Lines

For each medicine:

| Column | Meaning |
|--------|---------|
| Item | Search like sales (Enter). Ledger: Ctrl+L |
| Batch | Supplier batch number (required) |
| Exp M / Exp Y | Expiry month and year |
| Quantity | Paid qty |
| Free | Scheme / bonus qty (adds to stock, not to bill value) |
| Cost | Purchase rate (before GST as entered on the bill) |
| MRP / Sale price | Selling prices for this batch |
| GST % | Tax on the line |
| Disc % | Line discount |

**New batches** copy the medicine’s **rack** from master when the batch has no rack yet.

### Scan bill (OCR)

**Scan bill** photographs or loads a supplier invoice. Windows OCR always works; **Gemini** (Preferences + API key) reads mixed formats better. Match each scanned line to a medicine (or create mapping). Then review quantities and rates before save.

### Save (F9)

Stock increases, supplier ledger updates, a purchase invoice number is generated (`PUR-yyyyMMdd-####`). Bills lock after save; unlock to edit if allowed.

**Esc** starts a **new** purchase (clears the form).

### Open an old purchase

Search purchase bills (permission: search purchases). Reprint or unlock/edit.

---

## 8. Purchase orders

**Purchase Order** is an **order to the supplier**, not stock in.

1. Choose supplier and lines (medicine, qty, expected rate).
2. Save the PO.
3. When goods arrive, receive them on **Purchase** (link to PO if the screen offers it, or enter the GRN independently).

Use POs to plan replenishment from the low-stock report or shortage book.

---

## 9. Purchase returns

Return goods **to the supplier** against a purchase bill (damaged, wrong item, short expiry that is not the dedicated expiry-claim flow).

1. **F3 / Search bill** — find the purchase.
2. Enter return qty per line (cannot exceed received, net of earlier returns).
3. **F9 Process return**. Stock goes out; supplier **credit** (you owe them less / they owe you).

This is bill-linked. For **expiry sent to company without digging up every GRN**, use **Expiry to company**.

---

## 10. Expiry to company

Dedicated workflow: near-expired or expired **batches**, claim quantity, supplier credit, then their **credit note**.

### Tab: Eligible batches

1. Choose time **window** (e.g. already expired, 30/60/90 days).
2. Optional **supplier** filter. **No supplier** lists batches the system could not match (opening stock, unmapped MedWin, etc.).
3. **Refresh**.
4. Set **Claim qty** (or **Claim all stock**).
5. If **Supplier** is blank, pick the company in the dropdown.
6. Submit. PharmaPOS:

   - Removes claimed stock.
   - Creates a **purchase return** of kind “expiry to company”.
   - Opens a **claim** (`EXP-yyyyMMdd-####`) with expected credit.

### Tab: Claims / credit notes

When the company sends a credit note:

- Open the claim.
- Enter **CN number**, **date**, **amount**.
- Attach / save. Status moves from pending to settled.

Always confirm supplier before submit so credit sits on the right party.

---

## 11. Inventory

### On Hand

Live **batch** list: medicine, generic, batch, expiry, qty, cost, MRP, **rack/bin**, flags (low / near expiry / expired).

- Search by name, generic, barcode, **rack**, or **bin**.
- Filters: All, In stock, Low stock, Near expiry, Expired, Zero stock.
- KPIs: medicines, batches, quantity, stock value, alert counts.

### Ledger

Stock **movement history**: purchase in, sale out, returns, adjustments, transfers, with running balance. Search by medicine.

### Adjustment

Physical count vs system qty (shortage, excess, damage, expiry write-off). Pick medicine and batch (including zero-qty batches). Save posts **adjustment in/out** and a numbered adjustment voucher. Needs **inventory adjust** permission.

### Transfer

Move stock **between branches** (if you use more than one store). Pack / send / receive workflow with statuses. Needs **transfer** permission. Cancel if the shipment never left.

### Shortage book

Lost-sale list from billing (no stock) or manual add.

- Status: Open → Ordered → Fulfilled / Cancelled.
- Use it to build the next purchase order.

---

## 12. Masters

Master data the rest of the app uses. Search is prefix-first, then contains (type at least 2 characters).

### Suppliers

Name, GSTIN, drug licence, contact, address, payment terms (days). Status Active / Inactive.

### Customers

Name, phone, type (retail / etc.), GSTIN, address, **credit limit**. Used on sales and dues.

### Doctors

Name, qualification, specialisation, registration, hospital, phone. Used on sales and Schedule H/H1 register.

### Manufacturers

Company that makes the medicine (not the same as supplier/distributor).

### Employees

Code, name, designation, phone, salary, commission % (if you use it), status.

### Medicines

This is the SKU staff search at the counter.

| Field | Why it matters |
|--------|----------------|
| Name * | Search and invoice print |
| Generic / salt | Substitutes and search |
| Brand | Display |
| Manufacturer | Optional |
| Barcode | Scanner; **Generate** if empty; camera / image decode |
| HSN, GST % | Tax on bills |
| MRP, purchase, selling, default discount | Defaults for new batches / sales |
| Reorder level / qty | Low-stock report |
| **Rack** and **Bin** | Where the pack sits |
| Schedule | None / H / H1 / X / G / OTC |
| Prescription required | Flag on sale |
| Status | Inactive hides from search |

**Save Medicine**. Empty batch racks are filled from the medicine rack.

**Barcode**: generate, preview/print label, scan with camera or from a photo.

---

## 13. Accounting

### Parties

Suppliers (and similar parties) with **outstanding**. Select a party to see unpaid purchase invoices. Record payments (vouchers) against those bills.

### Customer dues

Customers with **balance due**. Select to see unpaid sale invoices. **Collect** cash/UPI/etc., print a collection receipt. Apply amounts to one or more invoices.

### Vouchers

Payment / receipt / expense vouchers (permission: record vouchers). Select invoices and **Apply** amounts.

### Cash book

Dated cash movements with running balance.

### Journal

Double-entry journal (permission: post journal). Chart of accounts is seeded (sales, purchase, GST, cash, bank, debtors, creditors). Day-to-day shops mostly use Sales, Purchase, and Collect — journal is for accountants.

---

## 14. Reports

Open **Reports**, pick a report, set **from / to** dates (stock reports are as-on-date), optional search box, **Run**. **Export CSV** if you have export permission. GST return types also export **JSON** and **Excel**.

| Report | What you get |
|--------|----------------|
| **Sales Report** | Completed invoices in the period (number, customer, totals). |
| **Purchase Report** | Received GRNs / purchase invoices. |
| **GST Summary** | Output vs input GST, invoice-wise. On-screen check, not a GSTN file. |
| **GSTR-1 export** | B2B, B2CS, CDNR, HSN from **sales**. JSON in GSTN-style shape + Excel sheets. File on GST portal after your CA reviews it. |
| **GSTR-2B worksheet** | Inward invoices and ITC by rate from **purchases**. Worksheet only — **not** the official GSTN 2B download. |
| **Gross Profit** | Revenue vs estimated cost per invoice. |
| **Sales by Medicine** | Qty and revenue ranked by SKU. |
| **Schedule H / H1 Register** | Inspector-style: date, patient, doctor, qty, invoice for scheduled sales. Filter H vs H1. Print/preview. |
| **Stock Valuation** | Current stock at purchase cost. |
| **Expiry Report** | Expired and 1–12 month horizon; supplier where known. |
| **Low Stock** | At or below reorder level. |
| **Sale Returns** | Return transactions in the period. |
| **Medicine-wise Returns** | Returned qty by medicine and batch. |

Click a sales/purchase row where the UI allows it to **open the bill**.

---

## 15. Settings

Visible tabs depend on permissions.

### Company

Legal identity printed on bills:

- Name, address, city, state, PIN, phone, email, website.
- **GSTIN**, **drug licence**, **PAN**.
- Logo path.
- **UPI VPA** — e.g. `yourshop@okicici`. Enables the UPI QR on sale bills. Leave blank to hide QR.
- Invoice **footer** (thank-you line).
- Currency (normally INR).

### Branches

Multiple stores: code, name, address, GST/DL, head office flag. Users are tied to a branch. Stock and invoices are branch-scoped.

### Counters

Billing desks: code, name, **default counter**. Day close is per counter.

### Preferences

- **Near-expiry days** and **default low-stock threshold**.
- Sales / purchase **invoice prefixes**.
- **Default bill paper**: A4, A5, thermal 80 mm, thermal 58 mm.
- **Allow editing** sale / purchase bills (cashiers). Admins with full access can still unlock.
- **Gemini** for purchase bill scan (API key stored on this PC only).
- **WhatsApp / SMS** bill share and “ask after save”.
- Panel widths / layout reset.
- Optional **MySQL reporting** sync (vendor-hosted analytics) — host, database, user; test connection.

### Medicine mapping

Maps **MedWin** (or import) medicine names to PharmaPOS SKUs so history and stock stay one catalogue. Pending vs applied mappings; search both sides.

### MedWin import

Tools to import an old MedWin database (vendor/advanced). Run only with a backup. Follow on-screen steps; do not import twice without guidance.

### Roles & permissions

Pick a role, tick permissions, Save. Users must **sign in again** to pick up changes. Built-in roles: Super Admin, Admin, Manager, Pharmacist, Cashier (exact names as seeded). **Restore defaults** resets that role’s ticks to factory.

### Users

Create users: username, full name, role, branch, phone, email, status. Set / reset password. Inactive users cannot log in.

### My password

Logged-in user changes their own password.

### Appearance

Theme related options (dark/light is also on the top bar).

### Backup

- **Backup now** — `.bak` on this PC.
- **Restore from this computer** — replaces the current database. Stop billing first.
- **Restore from Google Drive** — after OAuth is connected.
- **Automatic backup** after data changes, on an interval, optional upload to a Drive folder **PharmaPOS Backups**. Google needs an OAuth Desktop client (not a Gmail password).

Restore is destructive. Keep a copy before restore.

### Shop updates

Checks the vendor update server and can install a newer Setup. Use when told a new version is published. Close PharmaPOS before the installer overwrites files.

---

## 16. Printing, paper sizes, and UPI QR

### Paper templates

| Size | Typical use |
|------|-------------|
| **A4** | Full GST invoice (all columns). |
| **A5** | Half page; fewer columns (item, qty, rate, amount). |
| **Thermal 80 mm** | 80 mm roll printer; compact receipt. |
| **Thermal 58 mm** | Narrow roll. |

Default is **Settings → Preferences**. Override on **Invoice preview** before Print. PDF share uses the default (or the size used when exporting).

In Windows print dialog, choose the matching physical printer. For thermal, install the printer as 80 mm or 58 mm.

### UPI QR

If **Company → UPI VPA** is filled, the bill shows **Scan to pay (UPI)** with:

- Payee VPA (static).
- Payee name (shop name).
- **Amount** = bill grand total.
- Note = invoice number.

Any UPI app that understands `upi://pay` can scan it. This is **not** a PSP dynamic QR per transaction; settlement is to that VPA.

---

## 17. Keyboard shortcuts

| Key | Action |
|-----|--------|
| **F2** | Open Sales |
| **Enter** (Item cell) | Search medicine |
| **F3** | Customer / save customer (Sales); search bill (Purchase return) |
| **F4** | Medicine details |
| **F5** | Substitute |
| **F6** | Refill from last sale |
| **F8** | Sale return |
| **F9** | Save / print (Sales, Purchase); process return |
| **Esc** | Clear unsaved sale; new purchase |
| **Ctrl+T** | New customer bill |
| **Ctrl+1–4** | Switch bill |
| **Ctrl+W** | Close parked bill |
| **Ctrl+L** | Medicine ledger (Sales, Purchase, Inventory, Masters) |
| **Ctrl+L** in medicine search | Ledger for highlighted row |

---

## 18. Roles and permissions

Menus hide if the role cannot access the **module**. Buttons hide if a **fine-grained** permission is missing (e.g. discount, unlock, journal).

**Manage** (e.g. Full Sales Access) includes all actions in that module.

| Area | Typical permissions |
|------|---------------------|
| Sales | View, create, edit, unlock, discount, print, returns, high-value return, override policy |
| Purchase | View, create, edit, unlock, search, returns |
| Inventory | View, adjust, transfer |
| Masters | View, edit |
| Accounting | View, vouchers, journal |
| Reports | View, export |
| Settings | Company, branches, preferences |
| Security | View users, edit users, roles |

After changing roles, the user must **log in again**.

Do not share the Super Admin password. Create a **Cashier** user for the counter.

---

## 19. Daily / weekly / monthly routines

### Opening

1. Log in, select counter if asked.
2. Glance at **Dashboard** (expiry, low stock, cash).
3. Open **Sales**.

### During the day

- Bill on Sales; F6 for regulars.
- Receive goods on **Purchase** as soon as the box is opened (correct batch and expiry).
- If a medicine is missing, confirm **rack/bin** on Masters and On Hand search.

### Closing

1. **Day close** on the counter (count cash).
2. Optional: backup if auto-backup is off.
3. Sign out.

### Weekly

- **Low stock** and **shortage book** → purchase order or purchase.
- **Expiry to company** for batches in the claim window.
- Collect **customer dues**.

### Monthly (GST / owner)

- **GSTR-1 export** and **GSTR-2B worksheet** for the tax period; CA files on GSTN.
- **GST Summary** and **Gross Profit**.
- **Schedule H / H1 Register** print if inspectors require it.
- Confirm **payables** and supplier CNs (including expiry claims).
- **Backup** off-site (Drive or copy `.bak`).

---

## 20. Problems and tips

**Cannot log in**  
Caps lock; user Inactive; wrong branch is not usually the issue (username is unique). Admin resets password under Users.

**Medicine not in search**  
Status must be **Active**. Type 2+ letters. Check spelling / generic. Create from website or Masters.

**Stock shows but sale says insufficient**  
You are on another **batch**, or another **branch**. Pick the batch with qty. Opening catalogue items may get a large dummy OPENING batch — prefer real purchase batches.

**No supplier on expiry row**  
Pick supplier in the grid. Improve data by receiving purchases with correct supplier and batch numbers.

**Print looks stretched / tiny**  
Wrong paper template vs printer. Use 80 mm template on an 80 mm printer. Install correct Windows paper size.

**UPI QR missing**  
Company VPA empty or invalid (must contain `@`).

**Bill will not edit**  
Locked. Unlock (permission) and Preferences “allow editing”.

**App will not start after update**  
Close all PharmaPOS windows. Run Setup again. LocalDB must be installed.

**Restore wiped today’s bills**  
Restore replaces the whole database. Always backup immediately before restore.

**Slow search on huge catalogue**  
Type a longer prefix. Mapping keeps one SKU per product after MedWin import.

**GST JSON rejected on portal**  
GSTR-1 export is a helper. Your CA must validate GSTIN, place of supply, and HSN before upload. GSTR-2B worksheet is **not** the portal 2B file.

---

*PharmaPOS user guide — shop operations. For database tables and developer setup see `docs/DATABASE.md` and the repository README.*
