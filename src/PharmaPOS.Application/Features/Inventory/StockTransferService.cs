using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Identity;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Application.Features.ReportingSync;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Inventory;

public sealed class StockTransferService : IStockTransferService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly IReportingSyncService _reportingSync;

    public StockTransferService(IUnitOfWork uow, IDateTimeProvider clock, IReportingSyncService reportingSync)
    {
        _uow = uow;
        _clock = clock;
        _reportingSync = reportingSync;
    }

    public async Task<string> PreviewNextTransferNumberAsync(int? fromBranchId, CancellationToken ct = default)
    {
        var today = _clock.Today;
        var tomorrow = today.AddDays(1);
        var q = _uow.Repository<StockTransfer>().Query()
            .Where(t => t.Kind == StockTransferKind.Outbound
                        && t.TransferDate >= today && t.TransferDate < tomorrow);
        if (fromBranchId.HasValue) q = q.Where(t => t.BranchId == fromBranchId);

        var todayCount = await q.CountAsync(ct);
        return $"TRF-{today:yyyyMMdd}-{todayCount + 1:D4}";
    }

    public async Task<List<StockTransferBranchOptionDto>> ListDestinationBranchesAsync(
        int? fromBranchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<Branch>().Query().AsNoTracking()
            .Where(b => b.Status == EntityStatus.Active);

        if (fromBranchId.HasValue)
            q = q.Where(b => b.Id != fromBranchId.Value);

        return await q.OrderBy(b => b.Name)
            .Select(b => new StockTransferBranchOptionDto(b.Id, b.Code, b.Name))
            .ToListAsync(ct);
    }

    public async Task<Result<StockTransferReceiptDto>> CreateOutboundTransferAsync(
        CreateStockTransferRequest request,
        int? fromBranchId,
        int? userId,
        CancellationToken ct = default)
    {
        if (fromBranchId is null or <= 0)
            return Result.Failure<StockTransferReceiptDto>("Your user is not assigned to a branch.");

        if (request.ToBranchId <= 0)
            return Result.Failure<StockTransferReceiptDto>(
                "Select the destination store. Add other stores under Settings → Branches (same Code on both PCs).");

        if (request.ToBranchId == fromBranchId.Value)
            return Result.Failure<StockTransferReceiptDto>("Source and destination stores must be different.");

        var lines = request.Lines.Where(l => l.Quantity > 0).ToList();
        if (lines.Count == 0)
            return Result.Failure<StockTransferReceiptDto>("Add at least one medicine with quantity to transfer.");

        if (lines.GroupBy(l => l.SourceMedicineBatchId).Any(g => g.Count() > 1))
            return Result.Failure<StockTransferReceiptDto>("Each batch can appear only once on a transfer.");

        try
        {
            var receipt = await _uow.ExecuteInTransactionAsync(async token =>
            {
                var fromBranch = await _uow.Repository<Branch>().GetByIdAsync(fromBranchId.Value, token)
                    ?? throw new TransferException("Source store was not found.");
                var toBranch = await _uow.Repository<Branch>().GetByIdAsync(request.ToBranchId, token)
                    ?? throw new TransferException("Destination store was not found.");

                if (toBranch.Status != EntityStatus.Active)
                    throw new TransferException("Destination store is not active.");

                var transferNumber = await GenerateTransferNumberAsync(fromBranchId, StockTransferKind.Outbound, token);
                var packageKey = Guid.NewGuid().ToString("N");
                var transfer = new StockTransfer
                {
                    BranchId = fromBranchId,
                    ToBranchId = request.ToBranchId,
                    Kind = StockTransferKind.Outbound,
                    Status = StockTransferStatus.Active,
                    TransferNumber = transferNumber,
                    TransferDate = request.TransferDate.Date == default ? _clock.Today : request.TransferDate.Date,
                    FromBranchCode = fromBranch.Code,
                    FromBranchName = fromBranch.Name,
                    ToBranchCode = toBranch.Code,
                    ToBranchName = toBranch.Name,
                    PackageKey = packageKey,
                    Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
                    TransferredByUserId = userId
                };
                await _uow.Repository<StockTransfer>().AddAsync(transfer, token);
                await _uow.SaveChangesAsync(token);

                decimal totalQty = 0;
                var packageLines = new List<StockTransferPackageLine>();

                foreach (var line in lines)
                {
                    var sourceBatch = await _uow.Repository<MedicineBatch>().Query()
                        .Include(b => b.Medicine)
                        .FirstOrDefaultAsync(b => b.Id == line.SourceMedicineBatchId, token)
                        ?? throw new TransferException("A selected batch no longer exists.");

                    if (sourceBatch.BranchId != fromBranchId)
                        throw new TransferException($"Batch {sourceBatch.BatchNumber} does not belong to your store.");

                    if (sourceBatch.MedicineId != line.MedicineId)
                        throw new TransferException("Medicine/batch mismatch on a transfer line.");

                    if (line.Quantity > sourceBatch.QuantityAvailable)
                        throw new TransferException(
                            $"Insufficient stock for batch {sourceBatch.BatchNumber}. " +
                            $"Available: {sourceBatch.QuantityAvailable:N0}, requested: {line.Quantity:N0}.");

                    var medicine = sourceBatch.Medicine
                        ?? await _uow.Repository<Medicine>().GetByIdAsync(sourceBatch.MedicineId, token)
                        ?? throw new TransferException("Medicine not found for a transfer line.");

                    sourceBatch.QuantityAvailable -= line.Quantity;
                    _uow.Repository<MedicineBatch>().Update(sourceBatch);

                    await _uow.Repository<StockTransferItem>().AddAsync(new StockTransferItem
                    {
                        StockTransferId = transfer.Id,
                        MedicineId = sourceBatch.MedicineId,
                        SourceMedicineBatchId = sourceBatch.Id,
                        MedicineName = medicine.Name,
                        MedicineBarcode = medicine.Barcode,
                        BatchNumber = sourceBatch.BatchNumber,
                        ExpiryDate = sourceBatch.ExpiryDate,
                        ManufacturingDate = sourceBatch.ManufacturingDate,
                        Quantity = line.Quantity,
                        PurchasePrice = sourceBatch.PurchasePrice,
                        Mrp = sourceBatch.Mrp,
                        SellingPrice = sourceBatch.SellingPrice,
                        GstPercent = sourceBatch.GstPercent,
                        RackNumber = sourceBatch.RackNumber
                    }, token);

                    await _uow.Repository<StockMovement>().AddAsync(new StockMovement
                    {
                        BranchId = fromBranchId,
                        MedicineId = sourceBatch.MedicineId,
                        MedicineBatchId = sourceBatch.Id,
                        MovementType = StockMovementType.TransferOut,
                        Quantity = -line.Quantity,
                        BalanceAfter = sourceBatch.QuantityAvailable,
                        UnitCost = sourceBatch.PurchasePrice,
                        ReferenceType = nameof(StockTransfer),
                        ReferenceId = transfer.Id,
                        ReferenceNumber = transfer.TransferNumber,
                        MovementDateUtc = _clock.UtcNow,
                        Remarks = $"Package to {toBranch.Name}"
                    }, token);

                    packageLines.Add(new StockTransferPackageLine
                    {
                        MedicineName = medicine.Name,
                        MedicineBarcode = medicine.Barcode,
                        GenericName = medicine.GenericName,
                        BatchNumber = sourceBatch.BatchNumber,
                        ExpiryDate = sourceBatch.ExpiryDate,
                        ManufacturingDate = sourceBatch.ManufacturingDate,
                        Quantity = line.Quantity,
                        PurchasePrice = sourceBatch.PurchasePrice,
                        Mrp = sourceBatch.Mrp,
                        SellingPrice = sourceBatch.SellingPrice,
                        GstPercent = sourceBatch.GstPercent,
                        RackNumber = sourceBatch.RackNumber
                    });

                    totalQty += line.Quantity;
                }

                await _uow.SaveChangesAsync(token);

                var package = new StockTransferPackage
                {
                    Format = StockTransferPackage.CurrentFormat,
                    PackageKey = packageKey,
                    TransferNumber = transfer.TransferNumber,
                    TransferDate = transfer.TransferDate,
                    FromBranchCode = fromBranch.Code,
                    FromBranchName = fromBranch.Name,
                    ToBranchCode = toBranch.Code,
                    ToBranchName = toBranch.Name,
                    Remarks = transfer.Remarks,
                    Voided = false,
                    Lines = packageLines
                };

                return new StockTransferReceiptDto
                {
                    TransferId = transfer.Id,
                    TransferNumber = transfer.TransferNumber,
                    TransferDate = transfer.TransferDate,
                    FromBranchName = fromBranch.Name,
                    ToBranchName = toBranch.Name,
                    LinesTransferred = lines.Count,
                    TotalQuantity = totalQty,
                    PackageKey = packageKey,
                    PackageJson = JsonSerializer.Serialize(package, JsonOptions),
                    SuggestedFileName = $"{SanitizeFilePart(fromBranch.Code)}_to_{SanitizeFilePart(toBranch.Code)}_{transfer.TransferNumber}.pharmatrf"
                };
            }, ct);

            await _reportingSync.EnqueueStockTransferAsync(receipt.TransferId, ct);
            return Result.Success(receipt);
        }
        catch (TransferException ex)
        {
            return Result.Failure<StockTransferReceiptDto>(ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<StockTransferReceiptDto>($"Could not complete transfer: {ex.Message}");
        }
    }

    public async Task<Result<string>> GetOutboundPackageJsonAsync(
        int transferId, int? branchId, CancellationToken ct = default)
    {
        var transfer = await _uow.Repository<StockTransfer>().Query().AsNoTracking()
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == transferId, ct);

        if (transfer is null)
            return Result.Failure<string>("Transfer not found.");
        if (transfer.Kind != StockTransferKind.Outbound)
            return Result.Failure<string>("Only sent transfers can be re-exported.");
        if (transfer.Status == StockTransferStatus.Cancelled)
            return Result.Failure<string>("This transfer was cancelled. Create a new transfer instead.");
        if (branchId.HasValue && transfer.BranchId != branchId)
            return Result.Failure<string>("Transfer belongs to another store.");

        var package = new StockTransferPackage
        {
            Format = StockTransferPackage.CurrentFormat,
            PackageKey = transfer.PackageKey,
            TransferNumber = transfer.TransferNumber,
            TransferDate = transfer.TransferDate,
            FromBranchCode = transfer.FromBranchCode,
            FromBranchName = transfer.FromBranchName,
            ToBranchCode = transfer.ToBranchCode,
            ToBranchName = transfer.ToBranchName,
            Remarks = transfer.Remarks,
            Voided = false,
            Lines = transfer.Items.Select(i => new StockTransferPackageLine
            {
                MedicineName = i.MedicineName,
                MedicineBarcode = i.MedicineBarcode,
                BatchNumber = i.BatchNumber,
                ExpiryDate = i.ExpiryDate,
                ManufacturingDate = i.ManufacturingDate,
                Quantity = i.Quantity,
                PurchasePrice = i.PurchasePrice,
                Mrp = i.Mrp,
                SellingPrice = i.SellingPrice,
                GstPercent = i.GstPercent,
                RackNumber = i.RackNumber
            }).ToList()
        };

        return Result.Success(JsonSerializer.Serialize(package, JsonOptions));
    }

    public async Task<Result<StockTransferReceiptDto>> CancelTransferAsync(
        int transferId,
        int? branchId,
        string? reason,
        CancellationToken ct = default)
    {
        if (branchId is null or <= 0)
            return Result.Failure<StockTransferReceiptDto>("Your user is not assigned to a branch.");

        try
        {
            var receipt = await _uow.ExecuteInTransactionAsync(async token =>
            {
                var transfer = await _uow.Repository<StockTransfer>().Query()
                    .Include(t => t.Items)
                    .FirstOrDefaultAsync(t => t.Id == transferId, token)
                    ?? throw new TransferException("Transfer not found.");

                if (transfer.BranchId != branchId)
                    throw new TransferException("Transfer belongs to another store.");
                if (transfer.Status == StockTransferStatus.Cancelled)
                    throw new TransferException("This transfer is already cancelled.");

                return transfer.Kind switch
                {
                    StockTransferKind.Outbound => await CancelOutboundCoreAsync(transfer, branchId.Value, reason, token),
                    StockTransferKind.Inbound => await CancelInboundCoreAsync(transfer, branchId.Value, reason, token),
                    _ => throw new TransferException("Unknown transfer type.")
                };
            }, ct);

            await _reportingSync.EnqueueStockTransferAsync(receipt.TransferId, ct);
            return Result.Success(receipt);
        }
        catch (TransferException ex)
        {
            return Result.Failure<StockTransferReceiptDto>(ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<StockTransferReceiptDto>($"Could not cancel transfer: {ex.Message}");
        }
    }

    private async Task<StockTransferReceiptDto> CancelOutboundCoreAsync(
        StockTransfer transfer, int branchId, string? reason, CancellationToken token)
    {
        decimal totalQty = 0;
        foreach (var item in transfer.Items)
        {
            if (item.SourceMedicineBatchId is not int sourceBatchId)
                throw new TransferException(
                    $"Cannot restore stock for {item.MedicineName} — source batch missing.");

            var batch = await _uow.Repository<MedicineBatch>().GetByIdAsync(sourceBatchId, token)
                ?? throw new TransferException(
                    $"Cannot restore stock for {item.MedicineName} / {item.BatchNumber} — batch was deleted.");

            if (batch.BranchId != branchId)
                throw new TransferException($"Batch {item.BatchNumber} no longer belongs to this store.");

            batch.QuantityAvailable += item.Quantity;
            _uow.Repository<MedicineBatch>().Update(batch);

            await _uow.Repository<StockMovement>().AddAsync(new StockMovement
            {
                BranchId = branchId,
                MedicineId = item.MedicineId,
                MedicineBatchId = batch.Id,
                MovementType = StockMovementType.TransferIn,
                Quantity = item.Quantity,
                BalanceAfter = batch.QuantityAvailable,
                UnitCost = item.PurchasePrice,
                ReferenceType = nameof(StockTransfer),
                ReferenceId = transfer.Id,
                ReferenceNumber = transfer.TransferNumber,
                MovementDateUtc = _clock.UtcNow,
                Remarks = "Cancelled outbound transfer — stock restored"
            }, token);

            totalQty += item.Quantity;
        }

        transfer.Status = StockTransferStatus.Cancelled;
        transfer.CancelledAtUtc = _clock.UtcNow;
        transfer.CancelReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by sender" : reason.Trim();
        _uow.Repository<StockTransfer>().Update(transfer);
        await _uow.SaveChangesAsync(token);

        var voidPackage = new StockTransferPackage
        {
            Format = StockTransferPackage.CurrentFormat,
            PackageKey = transfer.PackageKey,
            TransferNumber = transfer.TransferNumber,
            TransferDate = transfer.TransferDate,
            FromBranchCode = transfer.FromBranchCode,
            FromBranchName = transfer.FromBranchName,
            ToBranchCode = transfer.ToBranchCode,
            ToBranchName = transfer.ToBranchName,
            Remarks = transfer.CancelReason,
            Voided = true,
            Lines = []
        };

        return new StockTransferReceiptDto
        {
            TransferId = transfer.Id,
            TransferNumber = transfer.TransferNumber,
            TransferDate = transfer.TransferDate,
            FromBranchName = transfer.FromBranchName,
            ToBranchName = transfer.ToBranchName,
            LinesTransferred = transfer.Items.Count,
            TotalQuantity = totalQty,
            PackageKey = transfer.PackageKey,
            PackageJson = JsonSerializer.Serialize(voidPackage, JsonOptions),
            SuggestedFileName =
                $"{SanitizeFilePart(transfer.FromBranchCode)}_CANCEL_{transfer.TransferNumber}.pharmatrf",
            IsReturnPackage = false
        };
    }

    private async Task<StockTransferReceiptDto> CancelInboundCoreAsync(
        StockTransfer transfer, int branchId, string? reason, CancellationToken token)
    {
        var localBranch = await _uow.Repository<Branch>().GetByIdAsync(branchId, token)
            ?? throw new TransferException("Store not found.");

        decimal totalQty = 0;
        var returnLines = new List<StockTransferPackageLine>();

        foreach (var item in transfer.Items)
        {
            if (item.DestinationMedicineBatchId is not int destBatchId)
                throw new TransferException(
                    $"Cannot reverse {item.MedicineName} — destination batch missing.");

            var batch = await _uow.Repository<MedicineBatch>().GetByIdAsync(destBatchId, token)
                ?? throw new TransferException(
                    $"Cannot reverse {item.MedicineName} / {item.BatchNumber} — batch was deleted.");

            if (batch.BranchId != branchId)
                throw new TransferException($"Batch {item.BatchNumber} no longer belongs to this store.");

            if (batch.QuantityAvailable < item.Quantity)
                throw new TransferException(
                    $"Cannot cancel — not enough stock left for {item.MedicineName} / {item.BatchNumber}.\n" +
                    $"Need {item.Quantity:N0}, available {batch.QuantityAvailable:N0}.\n" +
                    "Some quantity may already have been sold.");

            batch.QuantityAvailable -= item.Quantity;
            _uow.Repository<MedicineBatch>().Update(batch);

            await _uow.Repository<StockMovement>().AddAsync(new StockMovement
            {
                BranchId = branchId,
                MedicineId = item.MedicineId,
                MedicineBatchId = batch.Id,
                MovementType = StockMovementType.TransferOut,
                Quantity = -item.Quantity,
                BalanceAfter = batch.QuantityAvailable,
                UnitCost = item.PurchasePrice,
                ReferenceType = nameof(StockTransfer),
                ReferenceId = transfer.Id,
                ReferenceNumber = transfer.TransferNumber,
                MovementDateUtc = _clock.UtcNow,
                Remarks = "Cancelled inbound transfer — stock removed"
            }, token);

            returnLines.Add(new StockTransferPackageLine
            {
                MedicineName = item.MedicineName,
                MedicineBarcode = item.MedicineBarcode,
                BatchNumber = item.BatchNumber,
                ExpiryDate = item.ExpiryDate,
                ManufacturingDate = item.ManufacturingDate,
                Quantity = item.Quantity,
                PurchasePrice = item.PurchasePrice,
                Mrp = item.Mrp,
                SellingPrice = item.SellingPrice,
                GstPercent = item.GstPercent,
                RackNumber = item.RackNumber
            });

            totalQty += item.Quantity;
        }

        transfer.Status = StockTransferStatus.Cancelled;
        transfer.CancelledAtUtc = _clock.UtcNow;
        transfer.CancelReason = string.IsNullOrWhiteSpace(reason)
            ? "Cancelled by receiving store"
            : reason.Trim();
        _uow.Repository<StockTransfer>().Update(transfer);
        await _uow.SaveChangesAsync(token);

        // Reverse package: receiving store sends stock back to original sender.
        var returnPackageKey = Guid.NewGuid().ToString("N");
        var returnPackage = new StockTransferPackage
        {
            Format = StockTransferPackage.CurrentFormat,
            PackageKey = returnPackageKey,
            TransferNumber = $"RET-{transfer.TransferNumber}",
            TransferDate = _clock.Today,
            FromBranchCode = localBranch.Code,
            FromBranchName = localBranch.Name,
            ToBranchCode = transfer.FromBranchCode,
            ToBranchName = transfer.FromBranchName,
            Remarks = $"Return after cancel of import {transfer.TransferNumber}",
            Voided = false,
            Lines = returnLines
        };

        return new StockTransferReceiptDto
        {
            TransferId = transfer.Id,
            TransferNumber = transfer.TransferNumber,
            TransferDate = transfer.TransferDate,
            FromBranchName = transfer.FromBranchName,
            ToBranchName = transfer.ToBranchName,
            LinesTransferred = transfer.Items.Count,
            TotalQuantity = totalQty,
            PackageKey = returnPackageKey,
            PackageJson = JsonSerializer.Serialize(returnPackage, JsonOptions),
            SuggestedFileName =
                $"{SanitizeFilePart(localBranch.Code)}_RETURN_{transfer.TransferNumber}.pharmatrf",
            IsReturnPackage = true
        };
    }

    public async Task<Result<StockTransferReceiptDto>> ImportPackageAsync(
        string packageJson,
        int? toBranchId,
        int? userId,
        CancellationToken ct = default)
    {
        if (toBranchId is null or <= 0)
            return Result.Failure<StockTransferReceiptDto>("Your user is not assigned to a branch.");

        StockTransferPackage package;
        try
        {
            package = JsonSerializer.Deserialize<StockTransferPackage>(packageJson, JsonOptions)
                      ?? throw new TransferException("Invalid transfer package.");
        }
        catch (JsonException)
        {
            return Result.Failure<StockTransferReceiptDto>("Invalid transfer package file.");
        }

        if (!string.Equals(package.Format, StockTransferPackage.CurrentFormat, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<StockTransferReceiptDto>($"Unsupported package format: {package.Format}");

        if (string.IsNullOrWhiteSpace(package.PackageKey) || package.Lines.Count == 0)
            return Result.Failure<StockTransferReceiptDto>("Transfer package is empty or incomplete.");

        try
        {
            var receipt = await _uow.ExecuteInTransactionAsync(async token =>
            {
                var already = await _uow.Repository<StockTransfer>().Query()
                    .AnyAsync(t => t.ExternalPackageKey == package.PackageKey, token);
                if (already)
                {
                    throw new TransferException(
                        $"This package was already processed ({package.TransferNumber}). " +
                        "It may have been imported or cancelled earlier.");
                }

                var localBranch = await _uow.Repository<Branch>().GetByIdAsync(toBranchId.Value, token)
                    ?? throw new TransferException("Receiving store was not found.");

                // Void / cancel notice from sending store — block the original package, add no stock.
                if (package.Voided)
                {
                    var voidTransferNumber = await GenerateTransferNumberAsync(
                        toBranchId, StockTransferKind.Inbound, token);
                    var voidStub = new StockTransfer
                    {
                        BranchId = toBranchId,
                        ToBranchId = toBranchId.Value,
                        Kind = StockTransferKind.Inbound,
                        Status = StockTransferStatus.Cancelled,
                        TransferNumber = voidTransferNumber,
                        TransferDate = package.TransferDate.Date == default ? _clock.Today : package.TransferDate.Date,
                        FromBranchCode = package.FromBranchCode,
                        FromBranchName = package.FromBranchName,
                        ToBranchCode = localBranch.Code,
                        ToBranchName = localBranch.Name,
                        PackageKey = Guid.NewGuid().ToString("N"),
                        ExternalPackageKey = package.PackageKey,
                        CancelledAtUtc = _clock.UtcNow,
                        CancelReason = $"Sender cancelled {package.TransferNumber}",
                        Remarks = $"Void notice for {package.TransferNumber}",
                        TransferredByUserId = userId
                    };
                    await _uow.Repository<StockTransfer>().AddAsync(voidStub, token);
                    await _uow.SaveChangesAsync(token);

                    return new StockTransferReceiptDto
                    {
                        TransferId = voidStub.Id,
                        TransferNumber = voidStub.TransferNumber,
                        TransferDate = voidStub.TransferDate,
                        FromBranchName = package.FromBranchName,
                        ToBranchName = localBranch.Name,
                        LinesTransferred = 0,
                        TotalQuantity = 0,
                        PackageKey = package.PackageKey,
                        SuggestedFileName = string.Empty
                    };
                }

                if (!string.IsNullOrWhiteSpace(package.ToBranchCode)
                    && !string.Equals(package.ToBranchCode.Trim(), localBranch.Code.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new TransferException(
                        $"This package is addressed to store code \"{package.ToBranchCode}\", " +
                        $"but you are logged into \"{localBranch.Code}\". " +
                        "Import it on the correct store PC, or fix Branch Code under Settings.");
                }

                var partner = await _uow.Repository<Branch>().Query()
                    .FirstOrDefaultAsync(b =>
                        b.Status == EntityStatus.Active
                        && b.Code == package.FromBranchCode
                        && b.Id != toBranchId.Value, token);

                var partnerId = partner?.Id ?? toBranchId.Value;
                var transferNumber = await GenerateTransferNumberAsync(toBranchId, StockTransferKind.Inbound, token);

                var transfer = new StockTransfer
                {
                    BranchId = toBranchId,
                    ToBranchId = partnerId,
                    Kind = StockTransferKind.Inbound,
                    Status = StockTransferStatus.Active,
                    TransferNumber = transferNumber,
                    TransferDate = package.TransferDate.Date == default ? _clock.Today : package.TransferDate.Date,
                    FromBranchCode = package.FromBranchCode,
                    FromBranchName = package.FromBranchName,
                    ToBranchCode = localBranch.Code,
                    ToBranchName = localBranch.Name,
                    PackageKey = Guid.NewGuid().ToString("N"),
                    ExternalPackageKey = package.PackageKey,
                    Remarks = string.IsNullOrWhiteSpace(package.Remarks)
                        ? $"Imported from {package.TransferNumber}"
                        : package.Remarks,
                    TransferredByUserId = userId
                };
                await _uow.Repository<StockTransfer>().AddAsync(transfer, token);
                await _uow.SaveChangesAsync(token);

                decimal totalQty = 0;
                var unmatched = new List<string>();

                foreach (var line in package.Lines.Where(l => l.Quantity > 0))
                {
                    var medicine = await ResolveMedicineAsync(line, token);
                    if (medicine is null)
                    {
                        unmatched.Add(string.IsNullOrWhiteSpace(line.MedicineBarcode)
                            ? line.MedicineName
                            : $"{line.MedicineName} ({line.MedicineBarcode})");
                        continue;
                    }

                    var destBatch = await _uow.Repository<MedicineBatch>().Query()
                        .FirstOrDefaultAsync(b =>
                            b.MedicineId == medicine.Id &&
                            b.BranchId == toBranchId &&
                            b.BatchNumber == line.BatchNumber, token);

                    if (destBatch is null)
                    {
                        destBatch = new MedicineBatch
                        {
                            MedicineId = medicine.Id,
                            BranchId = toBranchId,
                            BatchNumber = line.BatchNumber,
                            ManufacturingDate = line.ManufacturingDate,
                            ExpiryDate = line.ExpiryDate,
                            QuantityAvailable = line.Quantity,
                            PurchasePrice = line.PurchasePrice,
                            Mrp = line.Mrp,
                            SellingPrice = line.SellingPrice > 0 ? line.SellingPrice : line.Mrp,
                            GstPercent = line.GstPercent,
                            RackNumber = line.RackNumber
                        };
                        await _uow.Repository<MedicineBatch>().AddAsync(destBatch, token);
                        await _uow.SaveChangesAsync(token);
                    }
                    else
                    {
                        destBatch.QuantityAvailable += line.Quantity;
                        destBatch.PurchasePrice = line.PurchasePrice;
                        destBatch.Mrp = line.Mrp;
                        if (line.SellingPrice > 0) destBatch.SellingPrice = line.SellingPrice;
                        destBatch.GstPercent = line.GstPercent;
                        if (line.ExpiryDate.HasValue) destBatch.ExpiryDate = line.ExpiryDate;
                        if (line.ManufacturingDate.HasValue) destBatch.ManufacturingDate = line.ManufacturingDate;
                        _uow.Repository<MedicineBatch>().Update(destBatch);
                    }

                    await _uow.Repository<StockTransferItem>().AddAsync(new StockTransferItem
                    {
                        StockTransferId = transfer.Id,
                        MedicineId = medicine.Id,
                        DestinationMedicineBatchId = destBatch.Id,
                        MedicineName = medicine.Name,
                        MedicineBarcode = medicine.Barcode,
                        BatchNumber = line.BatchNumber,
                        ExpiryDate = line.ExpiryDate,
                        ManufacturingDate = line.ManufacturingDate,
                        Quantity = line.Quantity,
                        PurchasePrice = line.PurchasePrice,
                        Mrp = line.Mrp,
                        SellingPrice = line.SellingPrice,
                        GstPercent = line.GstPercent,
                        RackNumber = line.RackNumber
                    }, token);

                    await _uow.Repository<StockMovement>().AddAsync(new StockMovement
                    {
                        BranchId = toBranchId,
                        MedicineId = medicine.Id,
                        MedicineBatchId = destBatch.Id,
                        MovementType = StockMovementType.TransferIn,
                        Quantity = line.Quantity,
                        BalanceAfter = destBatch.QuantityAvailable,
                        UnitCost = line.PurchasePrice,
                        ReferenceType = nameof(StockTransfer),
                        ReferenceId = transfer.Id,
                        ReferenceNumber = transfer.TransferNumber,
                        MovementDateUtc = _clock.UtcNow,
                        Remarks = $"From {package.FromBranchName} ({package.TransferNumber})"
                    }, token);

                    totalQty += line.Quantity;
                }

                if (unmatched.Count > 0 && totalQty == 0)
                {
                    throw new TransferException(
                        "No medicines could be matched in this store's masters.\n" +
                        "Add these medicines (same barcode or exact name) then import again:\n• "
                        + string.Join("\n• ", unmatched.Take(12)));
                }

                if (unmatched.Count > 0)
                {
                    throw new TransferException(
                        "Import cancelled — some medicines were not found in masters:\n• "
                        + string.Join("\n• ", unmatched.Take(12))
                        + "\n\nAdd them under Masters (prefer matching barcode), then import the same file again.");
                }

                await _uow.SaveChangesAsync(token);

                return new StockTransferReceiptDto
                {
                    TransferId = transfer.Id,
                    TransferNumber = transfer.TransferNumber,
                    TransferDate = transfer.TransferDate,
                    FromBranchName = package.FromBranchName,
                    ToBranchName = localBranch.Name,
                    LinesTransferred = package.Lines.Count,
                    TotalQuantity = totalQty,
                    PackageKey = package.PackageKey,
                    SuggestedFileName = string.Empty
                };
            }, ct);

            await _reportingSync.EnqueueStockTransferAsync(receipt.TransferId, ct);
            return Result.Success(receipt);
        }
        catch (TransferException ex)
        {
            return Result.Failure<StockTransferReceiptDto>(ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<StockTransferReceiptDto>($"Could not import transfer: {ex.Message}");
        }
    }

    public async Task<List<StockTransferListRowDto>> ListRecentTransfersAsync(
        int? branchId, int take = 50, CancellationToken ct = default)
    {
        var q = _uow.Repository<StockTransfer>().Query().AsNoTracking()
            .Include(t => t.Items)
            .AsQueryable();

        if (branchId.HasValue)
            q = q.Where(t => t.BranchId == branchId);

        var rows = await q.OrderByDescending(t => t.TransferDate)
            .ThenByDescending(t => t.Id)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(t => new StockTransferListRowDto
        {
            TransferId = t.Id,
            TransferNumber = t.TransferNumber,
            TransferDate = t.TransferDate,
            FromBranchName = t.FromBranchName,
            ToBranchName = t.ToBranchName,
            LineCount = t.Items.Count,
            TotalQuantity = t.Items.Sum(i => i.Quantity),
            Remarks = t.Status == StockTransferStatus.Cancelled
                ? (t.CancelReason ?? t.Remarks)
                : t.Remarks,
            IsOutgoing = t.Kind == StockTransferKind.Outbound,
            IsCancelled = t.Status == StockTransferStatus.Cancelled,
            CanReExport = t.Kind == StockTransferKind.Outbound && t.Status == StockTransferStatus.Active,
            CanCancel = t.Status == StockTransferStatus.Active
                && (t.Kind == StockTransferKind.Outbound || t.Kind == StockTransferKind.Inbound)
        }).ToList();
    }

    public async Task<Result<StockTransferDetailDto>> GetTransferDetailsAsync(
        int transferId, int? branchId, CancellationToken ct = default)
    {
        var transfer = await _uow.Repository<StockTransfer>().Query().AsNoTracking()
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == transferId, ct);

        if (transfer is null)
            return Result.Failure<StockTransferDetailDto>("Transfer not found.");
        if (branchId.HasValue && transfer.BranchId != branchId)
            return Result.Failure<StockTransferDetailDto>("Transfer belongs to another store.");

        var isOutgoing = transfer.Kind == StockTransferKind.Outbound;
        var isCancelled = transfer.Status == StockTransferStatus.Cancelled;

        return Result.Success(new StockTransferDetailDto
        {
            TransferId = transfer.Id,
            TransferNumber = transfer.TransferNumber,
            TransferDate = transfer.TransferDate,
            FromBranchName = transfer.FromBranchName,
            ToBranchName = transfer.ToBranchName,
            DirectionLabel = isCancelled ? "Cancelled" : isOutgoing ? "Sent" : "Received",
            Remarks = isCancelled ? (transfer.CancelReason ?? transfer.Remarks) : transfer.Remarks,
            IsCancelled = isCancelled,
            Lines = transfer.Items
                .OrderBy(i => i.MedicineName)
                .ThenBy(i => i.BatchNumber)
                .Select(i => new StockTransferDetailLineDto
                {
                    MedicineName = i.MedicineName,
                    MedicineBarcode = i.MedicineBarcode,
                    BatchNumber = i.BatchNumber,
                    ExpiryDate = i.ExpiryDate,
                    Quantity = i.Quantity,
                    Mrp = i.Mrp,
                    PurchasePrice = i.PurchasePrice
                }).ToList()
        });
    }

    private async Task<Medicine?> ResolveMedicineAsync(StockTransferPackageLine line, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(line.MedicineBarcode))
        {
            var barcodeKey = SearchQueryExtensions.NormalizeTerm(line.MedicineBarcode);
            var byBarcode = await _uow.Repository<Medicine>().Query()
                .FirstOrDefaultAsync(m =>
                    m.Status == EntityStatus.Active
                    && m.BarcodeSearchKey != ""
                    && m.BarcodeSearchKey == barcodeKey, ct);
            if (byBarcode is not null) return byBarcode;
        }

        var nameKey = SearchQueryExtensions.NormalizeTerm(line.MedicineName);
        if (nameKey.Length == 0) return null;

        return await _uow.Repository<Medicine>().Query()
            .FirstOrDefaultAsync(m =>
                m.Status == EntityStatus.Active && m.NameSearchKey == nameKey, ct);
    }

    private async Task<string> GenerateTransferNumberAsync(
        int? branchId, StockTransferKind kind, CancellationToken ct)
    {
        var today = _clock.Today;
        var tomorrow = today.AddDays(1);
        var prefix = kind == StockTransferKind.Inbound ? "TRI" : "TRF";
        var q = _uow.Repository<StockTransfer>().Query()
            .Where(t => t.Kind == kind && t.TransferDate >= today && t.TransferDate < tomorrow);
        if (branchId.HasValue) q = q.Where(t => t.BranchId == branchId);

        var todayCount = await q.CountAsync(ct);
        return $"{prefix}-{today:yyyyMMdd}-{todayCount + 1:D4}";
    }

    private static string SanitizeFilePart(string value)
    {
        var cleaned = string.Concat((value ?? "store").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        return string.IsNullOrWhiteSpace(cleaned) ? "store" : cleaned;
    }

    private sealed class TransferException : Exception
    {
        public TransferException(string message) : base(message) { }
    }

    private sealed class StockTransferPackage
    {
        public const string CurrentFormat = "PharmaPOS.StockTransfer.v1";
        public string Format { get; set; } = CurrentFormat;
        public string PackageKey { get; set; } = string.Empty;
        public string TransferNumber { get; set; } = string.Empty;
        public DateTime TransferDate { get; set; }
        public string FromBranchCode { get; set; } = string.Empty;
        public string FromBranchName { get; set; } = string.Empty;
        public string ToBranchCode { get; set; } = string.Empty;
        public string ToBranchName { get; set; } = string.Empty;
        public string? Remarks { get; set; }
        public bool Voided { get; set; }
        public List<StockTransferPackageLine> Lines { get; set; } = [];
    }

    private sealed class StockTransferPackageLine
    {
        public string MedicineName { get; set; } = string.Empty;
        public string? MedicineBarcode { get; set; }
        public string? GenericName { get; set; }
        public string BatchNumber { get; set; } = string.Empty;
        public DateTime? ExpiryDate { get; set; }
        public DateTime? ManufacturingDate { get; set; }
        public decimal Quantity { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal Mrp { get; set; }
        public decimal SellingPrice { get; set; }
        public decimal GstPercent { get; set; }
        public string? RackNumber { get; set; }
    }
}
