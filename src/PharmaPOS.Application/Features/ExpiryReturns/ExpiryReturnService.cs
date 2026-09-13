using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.PurchaseReturns;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.ExpiryReturns;

public sealed class ExpiryReturnService : IExpiryReturnService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ISettingsService _settings;
    private readonly IPurchaseReturnService _purchaseReturns;
    private readonly IFinancialYearContext _financialYear;

    public ExpiryReturnService(
        IUnitOfWork uow,
        IDateTimeProvider clock,
        ISettingsService settings,
        IPurchaseReturnService purchaseReturns,
        IFinancialYearContext financialYear)
    {
        _uow = uow;
        _clock = clock;
        _settings = settings;
        _purchaseReturns = purchaseReturns;
        _financialYear = financialYear;
    }

    public async Task<List<ExpiryEligibleBatchDto>> ListEligibleBatchesAsync(
        int horizonDays,
        int? supplierId,
        int? branchId,
        CancellationToken ct = default)
    {
        var today = _clock.Today.Date;
        var prefs = await _settings.GetPreferencesAsync(ct);
        IQueryable<MedicineBatch> q = _uow.Repository<MedicineBatch>().Query().AsNoTracking()
            .Include(b => b.Medicine)
            .Where(b => b.QuantityAvailable > 0
                        && b.ExpiryDate != null
                        && b.Medicine != null
                        && b.Medicine.Status == EntityStatus.Active);

        if (horizonDays == 0)
            q = q.Where(b => b.ExpiryDate!.Value.Date < today);
        else
        {
            var days = horizonDays > 0 ? horizonDays : (prefs.NearExpiryDays < 1 ? 90 : prefs.NearExpiryDays);
            var cutoff = today.AddDays(days);
            q = q.Where(b => b.ExpiryDate!.Value.Date <= cutoff);
        }
        if (branchId.HasValue) q = q.Where(b => b.BranchId == branchId);

        var batches = await q.OrderBy(b => b.ExpiryDate).ToListAsync(ct);
        var resolved = await ResolvePurchaseSourceAsync(
            batches.Select(b => (b.Id, b.MedicineId, b.Medicine!.Name, b.BatchNumber)).ToList(), ct);

        var rows = new List<ExpiryEligibleBatchDto>();
        foreach (var b in batches)
        {
            resolved.TryGetValue(b.Id, out var src);
            if (supplierId == -1)
            {
                if (src.SupplierId is > 0) continue;
            }
            else if (supplierId is > 0 && src.SupplierId != supplierId)
                continue;

            var expiry = b.ExpiryDate!.Value.Date;
            var status = expiry < today
                ? "Expired"
                : expiry <= today.AddDays(30)
                    ? "Within 30 days"
                    : $"Expires {expiry:dd-MMM-yyyy}";

            rows.Add(new ExpiryEligibleBatchDto(
                b.Id,
                b.MedicineId,
                b.Medicine!.Name,
                b.BatchNumber,
                expiry,
                b.QuantityAvailable,
                b.PurchasePrice,
                b.GstPercent,
                Math.Round(b.PurchasePrice * b.QuantityAvailable, 2),
                status,
                src.SupplierId,
                src.SupplierName,
                src.PurchaseId,
                src.InvoiceNumber));
        }

        return rows;
    }

    public Task<List<ExpirySupplierOptionDto>> ListSuppliersAsync(CancellationToken ct = default)
    {
        // Only suppliers that have at least one received purchase bill.
        var supplierIdsWithPurchases = _uow.Repository<Purchase>().Query().AsNoTracking()
            .Where(p => p.Status != PurchaseStatus.Cancelled && p.Status != PurchaseStatus.Draft)
            .Select(p => p.SupplierId)
            .Distinct();

        return _uow.Repository<Supplier>().Query().AsNoTracking()
            .Where(s => s.Status == EntityStatus.Active && supplierIdsWithPurchases.Contains(s.Id))
            .OrderBy(s => s.Name)
            .Select(s => new ExpirySupplierOptionDto(s.Id, s.Name))
            .ToListAsync(ct);
    }

    public async Task<Result<ExpiryClaimReceiptDto>> SubmitClaimAsync(
        SubmitExpiryClaimRequest request, int? branchId, string? userName, CancellationToken ct = default)
    {
        if (!_financialYear.CanEditTransactions)
            return FinancialYearGuard.FailIfReadOnly<ExpiryClaimReceiptDto>(_financialYear);
        if (request.SupplierId <= 0)
            return Result.Failure<ExpiryClaimReceiptDto>("Select a supplier.");

        var lines = request.Lines.Where(l => l.ClaimQuantity > 0).ToList();
        if (lines.Count == 0)
            return Result.Failure<ExpiryClaimReceiptDto>("Enter claim quantity on at least one batch.");

        var supplier = await _uow.Repository<Supplier>().GetByIdAsync(request.SupplierId, ct);
        if (supplier is null)
            return Result.Failure<ExpiryClaimReceiptDto>("Supplier not found.");

        var expiredReasonId = await _uow.Repository<ReturnReason>().Query().AsNoTracking()
            .Where(r => r.Code == "EXPIRED" && r.IsActive)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct);

        var eligible = await ListEligibleBatchesAsync(3650, null, branchId, ct);
        var byBatch = eligible.ToDictionary(e => e.MedicineBatchId);
        var prLines = new List<CreateDirectPurchaseReturnLineRequest>();
        var claimLines = new List<(ExpiryEligibleBatchDto Src, decimal Qty)>();

        foreach (var line in lines)
        {
            if (!byBatch.TryGetValue(line.MedicineBatchId, out var src))
                return Result.Failure<ExpiryClaimReceiptDto>(
                    "A selected batch is no longer eligible (sold or already claimed).");
            if (line.ClaimQuantity > src.StockQuantity)
                return Result.Failure<ExpiryClaimReceiptDto>(
                    $"Claim qty for {src.MedicineName} / {src.BatchNumber} exceeds stock ({src.StockQuantity:0.##}).");

            var lineSupplier = line.SupplierId is > 0 ? line.SupplierId : src.SupplierId;
            if (lineSupplier != request.SupplierId)
                return Result.Failure<ExpiryClaimReceiptDto>(
                    $"{src.MedicineName} / {src.BatchNumber} belongs to a different supplier. Filter to one supplier, or set the same supplier on each line.");
            if (lineSupplier is null or <= 0)
                return Result.Failure<ExpiryClaimReceiptDto>(
                    $"Choose a supplier for {src.MedicineName} / {src.BatchNumber}.");

            prLines.Add(new CreateDirectPurchaseReturnLineRequest
            {
                MedicineBatchId = src.MedicineBatchId,
                ReturnQuantity = line.ClaimQuantity,
                ReturnFreeQuantity = 0,
                PurchasePrice = src.PurchasePrice,
                DiscountPercent = 0,
                GstPercent = src.GstPercent,
                ReturnReasonId = expiredReasonId,
                ReasonRemarks = "Expiry-to-company claim"
            });
            claimLines.Add((src, line.ClaimQuantity));
        }

        var prResult = await _purchaseReturns.CreateDirectReturnAsync(
            new CreateDirectPurchaseReturnRequest
            {
                SupplierId = request.SupplierId,
                SettlementMode = PurchaseReturnSettlementMode.SupplierCredit,
                ReturnKind = PurchaseReturnKind.ExpiryToCompany,
                Remarks = string.IsNullOrWhiteSpace(request.Remarks)
                    ? "Expiry-to-company claim"
                    : request.Remarks.Trim(),
                Lines = prLines
            }, branchId, userName, ct);

        if (prResult.IsFailure || prResult.Value is null)
            return Result.Failure<ExpiryClaimReceiptDto>(prResult.Error ?? "Could not post stock return.");

        var claim = new ExpirySupplierClaim
        {
            ClaimNumber = await NextClaimNumberAsync(ct),
            ClaimDate = _clock.Now,
            SupplierId = request.SupplierId,
            Status = ExpiryClaimStatus.AwaitingCreditNote,
            PurchaseReturnId = prResult.Value.PurchaseReturnId,
            ExpectedCreditAmount = prResult.Value.GrandTotal,
            Remarks = request.Remarks,
            BranchId = branchId ?? supplier.BranchId,
            CreatedBy = userName
        };
        await _uow.Repository<ExpirySupplierClaim>().AddAsync(claim, ct);
        await _uow.SaveChangesAsync(ct);

        foreach (var (src, qty) in claimLines)
        {
            var unit = src.StockQuantity > 0
                ? src.StockValue / src.StockQuantity
                : src.PurchasePrice * (1 + src.GstPercent / 100m);
            await _uow.Repository<ExpirySupplierClaimItem>().AddAsync(new ExpirySupplierClaimItem
            {
                ClaimId = claim.Id,
                MedicineId = src.MedicineId,
                MedicineBatchId = src.MedicineBatchId,
                PurchaseId = src.PurchaseId,
                BatchNumber = src.BatchNumber,
                ExpiryDate = src.ExpiryDate,
                StockQuantity = src.StockQuantity,
                ClaimQuantity = qty,
                PurchasePrice = src.PurchasePrice,
                GstPercent = src.GstPercent,
                LineTotal = Math.Round(unit * qty, 2),
                CreatedBy = userName
            }, ct);
        }

        await _uow.SaveChangesAsync(ct);

        return Result.Success(new ExpiryClaimReceiptDto
        {
            ClaimId = claim.Id,
            ClaimNumber = claim.ClaimNumber,
            ReturnNumber = prResult.Value.ReturnNumber,
            SupplierName = prResult.Value.SupplierName,
            ExpectedCreditAmount = prResult.Value.GrandTotal,
            LineCount = claimLines.Count
        });
    }

    public async Task<List<ExpiryClaimListRowDto>> ListClaimsAsync(
        bool awaitingCreditNoteOnly, int? branchId, int take = 100, CancellationToken ct = default)
    {
        var q = _uow.Repository<ExpirySupplierClaim>().Query().AsNoTracking()
            .WhereInFinancialYear(_financialYear.Active, c => c.ClaimDate);
        if (branchId.HasValue) q = q.Where(c => c.BranchId == branchId);
        if (awaitingCreditNoteOnly)
            q = q.Where(c => c.Status == ExpiryClaimStatus.AwaitingCreditNote);

        return await q.OrderByDescending(c => c.ClaimDate).Take(take)
            .Select(c => new ExpiryClaimListRowDto(
                c.Id,
                c.ClaimNumber,
                c.ClaimDate,
                c.Supplier != null ? c.Supplier.Name : "—",
                c.ExpectedCreditAmount,
                c.Status == ExpiryClaimStatus.CreditReceived ? "Credit received"
                    : c.Status == ExpiryClaimStatus.Cancelled ? "Cancelled"
                    : "Awaiting credit note",
                c.CreditNoteNumber,
                c.CreditNoteDate,
                c.PurchaseReturn != null ? c.PurchaseReturn.ReturnNumber : ""))
            .ToListAsync(ct);
    }

    public async Task<Result<ExpiryClaimDetailDto>> GetClaimAsync(
        int claimId, int? branchId, CancellationToken ct = default)
    {
        var claim = await _uow.Repository<ExpirySupplierClaim>().Query().AsNoTracking()
            .Include(c => c.Supplier)
            .Include(c => c.PurchaseReturn)
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == claimId, ct);

        if (claim is null)
            return Result.Failure<ExpiryClaimDetailDto>("Claim not found.");
        if (branchId.HasValue && claim.BranchId != branchId)
            return Result.Failure<ExpiryClaimDetailDto>("Claim belongs to another branch.");

        var medIds = claim.Items.Select(i => i.MedicineId).Distinct().ToList();
        var names = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => medIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var purchaseIds = claim.Items.Where(i => i.PurchaseId.HasValue).Select(i => i.PurchaseId!.Value).Distinct().ToList();
        var invoices = purchaseIds.Count == 0
            ? new Dictionary<int, string>()
            : await _uow.Repository<Purchase>().Query().AsNoTracking()
                .Where(p => purchaseIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.InvoiceNumber, ct);

        return Result.Success(new ExpiryClaimDetailDto
        {
            Id = claim.Id,
            ClaimNumber = claim.ClaimNumber,
            ClaimDate = claim.ClaimDate,
            SupplierName = claim.Supplier?.Name ?? "—",
            ReturnNumber = claim.PurchaseReturn?.ReturnNumber ?? "",
            ExpectedCreditAmount = claim.ExpectedCreditAmount,
            Status = claim.Status,
            CreditNoteNumber = claim.CreditNoteNumber,
            CreditNoteDate = claim.CreditNoteDate,
            CreditNoteAmount = claim.CreditNoteAmount,
            Remarks = claim.Remarks,
            Lines = claim.Items.OrderBy(i => i.Id).Select(i => new ExpiryClaimDetailLineDto(
                names.TryGetValue(i.MedicineId, out var n) ? n : $"Medicine #{i.MedicineId}",
                i.BatchNumber,
                i.ExpiryDate,
                i.StockQuantity,
                i.ClaimQuantity,
                i.PurchasePrice,
                i.LineTotal,
                i.PurchaseId is int pid && invoices.TryGetValue(pid, out var inv) ? inv : null)).ToList()
        });
    }

    public async Task<Result> AttachCreditNoteAsync(
        AttachExpiryCreditNoteRequest request, string? userName, CancellationToken ct = default)
    {
        var fyBlock = FinancialYearGuard.EnsureEditable(_financialYear);
        if (fyBlock.IsFailure) return fyBlock;

        var number = request.CreditNoteNumber?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(number))
            return Result.Failure("Enter the supplier credit note number.");

        var claim = await _uow.Repository<ExpirySupplierClaim>().GetByIdAsync(request.ClaimId, ct);
        if (claim is null)
            return Result.Failure("Claim not found.");
        if (claim.Status == ExpiryClaimStatus.Cancelled)
            return Result.Failure("Cancelled claims cannot receive a credit note.");

        claim.CreditNoteNumber = number;
        claim.CreditNoteDate = request.CreditNoteDate?.Date ?? _clock.Now.Date;
        claim.CreditNoteAmount = request.CreditNoteAmount is > 0
            ? request.CreditNoteAmount
            : claim.ExpectedCreditAmount;
        claim.Status = ExpiryClaimStatus.CreditReceived;
        claim.ModifiedBy = userName;
        _uow.Repository<ExpirySupplierClaim>().Update(claim);
        await _uow.SaveChangesAsync(ct);

        await _purchaseReturns.AttachSupplierReceiptAsync(
            claim.PurchaseReturnId, number, claim.CreditNoteDate, userName, ct);

        return Result.Success();
    }

    private async Task<string> NextClaimNumberAsync(CancellationToken ct)
    {
        var day = _clock.Now.ToString("yyyyMMdd");
        var stem = $"EXP-{day}-";
        var last = await _uow.Repository<ExpirySupplierClaim>().Query().AsNoTracking()
            .Where(c => c.ClaimNumber.StartsWith(stem))
            .OrderByDescending(c => c.ClaimNumber)
            .Select(c => c.ClaimNumber)
            .FirstOrDefaultAsync(ct);

        var next = 1;
        if (last is not null && last.Length > stem.Length && int.TryParse(last[stem.Length..], out var n))
            next = n + 1;
        return $"{stem}{next:D4}";
    }

    private async Task<Dictionary<int, (int? SupplierId, string? SupplierName, int? PurchaseId, string? InvoiceNumber)>> ResolvePurchaseSourceAsync(
        List<(int BatchId, int MedicineId, string MedicineName, string BatchNumber)> batches,
        CancellationToken ct)
    {
        var result = new Dictionary<int, (int?, string?, int?, string?)>();
        if (batches.Count == 0) return result;

        var purchaseRaw = await (
            from i in _uow.Repository<PurchaseItem>().QueryIncludingDeleted().AsNoTracking()
            join p in _uow.Repository<Purchase>().QueryIncludingDeleted().AsNoTracking() on i.PurchaseId equals p.Id
            join s in _uow.Repository<Supplier>().QueryIncludingDeleted().AsNoTracking() on p.SupplierId equals s.Id
            where !i.IsDeleted && !p.IsDeleted && !s.IsDeleted
                  && p.Status != PurchaseStatus.Draft
                  && p.Status != PurchaseStatus.Cancelled
            select new
            {
                i.MedicineBatchId,
                i.MedicineId,
                i.BatchNumber,
                SupplierId = s.Id,
                SupplierName = s.Name,
                p.InvoiceDate,
                PurchaseId = p.Id,
                p.InvoiceNumber
            }).ToListAsync(ct);

        var purchaseRows = purchaseRaw.Select(p => new PurchaseHit(
            p.MedicineBatchId, p.MedicineId, p.BatchNumber, p.SupplierId, p.SupplierName,
            p.InvoiceDate, p.PurchaseId, p.InvoiceNumber)).ToList();

        var medicineIds = batches.Select(b => b.MedicineId)
            .Concat(purchaseRows.Select(p => p.MedicineId))
            .Distinct()
            .ToList();
        var names = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => medicineIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name })
            .ToListAsync(ct);
        var nameById = names.ToDictionary(m => m.Id, m => m.Name);

        var aliasIds = await LoadMedicineAliasesAsync(batches.Select(b => b.MedicineId).Distinct().ToList(), ct);

        var batchIds = batches.Select(b => b.BatchId).ToList();
        var movementHits = await (
            from m in _uow.Repository<StockMovement>().QueryIncludingDeleted().AsNoTracking()
            join p in _uow.Repository<Purchase>().QueryIncludingDeleted().AsNoTracking() on m.ReferenceId equals p.Id
            join s in _uow.Repository<Supplier>().QueryIncludingDeleted().AsNoTracking() on p.SupplierId equals s.Id
            where !m.IsDeleted && !p.IsDeleted && !s.IsDeleted
                  && m.MovementType == StockMovementType.PurchaseIn
                  && m.ReferenceType == nameof(Purchase)
                  && m.MedicineBatchId != null
                  && batchIds.Contains(m.MedicineBatchId.Value)
            select new
            {
                BatchId = m.MedicineBatchId!.Value,
                SupplierId = s.Id,
                s.Name,
                p.InvoiceDate,
                PurchaseId = p.Id,
                p.InvoiceNumber
            }).ToListAsync(ct);

        var byMovement = movementHits
            .GroupBy(x => x.BatchId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.PurchaseId).First());

        var byBatchId = purchaseRows
            .Where(p => p.MedicineBatchId is > 0)
            .GroupBy(p => p.MedicineBatchId!.Value)
            .ToDictionary(g => g.Key, g => Latest(g));

        var byMedicineBatch = purchaseRows
            .GroupBy(p => (p.MedicineId, Batch: NormBatch(p.BatchNumber)))
            .Where(g => g.Key.Batch.Length > 0)
            .ToDictionary(g => g.Key, g => Latest(g));

        var byNameBatch = purchaseRows
            .Select(p => (Hit: p, Name: NormName(nameById.GetValueOrDefault(p.MedicineId))))
            .Where(x => x.Name.Length > 0)
            .GroupBy(x => (x.Name, Batch: NormBatch(x.Hit.BatchNumber)))
            .Where(g => g.Key.Batch.Length > 0)
            .ToDictionary(g => g.Key, g => Latest(g.Select(x => x.Hit)));

        var byMedicineId = purchaseRows
            .GroupBy(p => p.MedicineId)
            .ToDictionary(g => g.Key, g => Latest(g));

        var byMedicineName = purchaseRows
            .Select(p => (Hit: p, Name: NormName(nameById.GetValueOrDefault(p.MedicineId))))
            .Where(x => x.Name.Length > 0)
            .GroupBy(x => x.Name)
            .ToDictionary(g => g.Key, g => Latest(g.Select(x => x.Hit)));

        var byBatchOnly = purchaseRows
            .GroupBy(p => NormBatch(p.BatchNumber))
            .Where(g => g.Key.Length >= 3 && g.Select(x => x.SupplierId).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => Latest(g));

        foreach (var batch in batches)
        {
            PurchaseHit? hit = null;
            var batchKey = NormBatch(batch.BatchNumber);
            var nameKey = NormName(batch.MedicineName);
            var ids = aliasIds.TryGetValue(batch.MedicineId, out var aliases)
                ? aliases
                : [batch.MedicineId];

            if (byBatchId.TryGetValue(batch.BatchId, out var viaId))
                hit = viaId;
            else if (byMovement.TryGetValue(batch.BatchId, out var viaMv))
                hit = new PurchaseHit(batch.BatchId, batch.MedicineId, batch.BatchNumber, viaMv.SupplierId, viaMv.Name, viaMv.InvoiceDate, viaMv.PurchaseId, viaMv.InvoiceNumber);
            else
            {
                foreach (var medId in ids)
                {
                    if (batchKey.Length > 0 && byMedicineBatch.TryGetValue((medId, batchKey), out var viaMedBatch))
                    {
                        hit = viaMedBatch;
                        break;
                    }
                }

                if (hit is null && nameKey.Length > 0 && batchKey.Length > 0
                    && byNameBatch.TryGetValue((nameKey, batchKey), out var viaNameBatch))
                    hit = viaNameBatch;

                if (hit is null)
                {
                    foreach (var medId in ids)
                    {
                        if (byMedicineId.TryGetValue(medId, out var viaMed))
                        {
                            hit = viaMed;
                            break;
                        }
                    }
                }

                if (hit is null && nameKey.Length > 0 && byMedicineName.TryGetValue(nameKey, out var viaName))
                    hit = viaName;

                if (hit is null && batchKey.Length >= 3 && byBatchOnly.TryGetValue(batchKey, out var viaBatch))
                    hit = viaBatch;
            }

            result[batch.BatchId] = hit is null
                ? (null, null, null, null)
                : (hit.SupplierId, hit.SupplierName, hit.PurchaseId, hit.InvoiceNumber);
        }

        return result;
    }

    private async Task<Dictionary<int, HashSet<int>>> LoadMedicineAliasesAsync(
        List<int> medicineIds, CancellationToken ct)
    {
        var map = medicineIds.ToDictionary(id => id, id => new HashSet<int> { id });
        if (medicineIds.Count == 0) return map;

        var links = await _uow.Repository<MedicineMedWinMapping>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => !m.IsDeleted
                        && (medicineIds.Contains(m.OneMgMedicineId)
                            || (m.MedWinMedicineId != null && medicineIds.Contains(m.MedWinMedicineId.Value))))
            .Select(m => new { m.OneMgMedicineId, m.MedWinMedicineId })
            .ToListAsync(ct);

        foreach (var link in links)
        {
            if (map.TryGetValue(link.OneMgMedicineId, out var a) && link.MedWinMedicineId is int mw)
                a.Add(mw);
            if (link.MedWinMedicineId is int medWin && map.TryGetValue(medWin, out var b))
                b.Add(link.OneMgMedicineId);
        }

        return map;
    }

    private static PurchaseHit Latest(IEnumerable<PurchaseHit> rows)
        => rows.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.PurchaseId).First();

    private static string NormBatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var chars = value.Where(char.IsLetterOrDigit).ToArray();
        return new string(chars).ToUpperInvariant();
    }

    private static string NormName(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    private sealed record PurchaseHit(
        int? MedicineBatchId,
        int MedicineId,
        string? BatchNumber,
        int SupplierId,
        string SupplierName,
        DateTime InvoiceDate,
        int PurchaseId,
        string InvoiceNumber);
}
