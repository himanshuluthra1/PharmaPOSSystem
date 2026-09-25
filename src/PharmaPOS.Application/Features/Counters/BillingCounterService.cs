using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Identity;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Entities.System;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Counters;

public sealed class BillingCounterService : IBillingCounterService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;

    public BillingCounterService(IUnitOfWork uow, IDateTimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<List<BillingCounterListDto>> ListAsync(int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<BillingCounter>().Query().AsNoTracking()
            .Where(c => !c.IsDeleted);
        if (branchId.HasValue)
            q = q.Where(c => c.BranchId == branchId);

        var counters = await q.OrderBy(c => c.Code).ToListAsync(ct);
        var open = await _uow.Repository<CounterSession>().Query().AsNoTracking()
            .Include(s => s.User)
            .Where(s => !s.IsDeleted && s.Status == CounterSessionStatus.Open)
            .ToListAsync(ct);

        return counters.Select(c =>
        {
            var session = open.FirstOrDefault(s => s.CounterId == c.Id);
            return new BillingCounterListDto(
                c.Id, c.Code, c.Name, c.IsDefault, c.Status, c.BranchId,
                session is not null, session?.User?.FullName);
        }).ToList();
    }

    public async Task<BillingCounterDetailDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var c = await _uow.Repository<BillingCounter>().Query().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (c is null) return null;
        return new BillingCounterDetailDto
        {
            Id = c.Id,
            Code = c.Code,
            Name = c.Name,
            IsDefault = c.IsDefault,
            Status = c.Status,
            BranchId = c.BranchId
        };
    }

    public async Task<Result<int>> SaveAsync(BillingCounterDetailDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Code))
            return Result.Failure<int>("Counter code is required.");
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure<int>("Counter name is required.");

        var code = dto.Code.Trim().ToUpperInvariant();
        var name = dto.Name.Trim();

        var duplicate = await _uow.Repository<BillingCounter>().Query()
            .AnyAsync(c => !c.IsDeleted && c.Code == code && c.BranchId == dto.BranchId && c.Id != dto.Id, ct);
        if (duplicate)
            return Result.Failure<int>($"Counter code '{code}' already exists for this branch.");

        try
        {
            if (dto.IsDefault)
            {
                var others = await _uow.Repository<BillingCounter>().Query()
                    .Where(c => !c.IsDeleted && c.BranchId == dto.BranchId && c.IsDefault && c.Id != dto.Id)
                    .ToListAsync(ct);
                foreach (var o in others)
                {
                    o.IsDefault = false;
                    _uow.Repository<BillingCounter>().Update(o);
                }
            }

            BillingCounter entity;
            if (dto.Id > 0)
            {
                entity = await _uow.Repository<BillingCounter>().Query()
                    .FirstOrDefaultAsync(c => c.Id == dto.Id && !c.IsDeleted, ct)
                    ?? throw new InvalidOperationException("Counter not found.");
                entity.Code = code;
                entity.Name = name;
                entity.IsDefault = dto.IsDefault;
                entity.Status = dto.Status;
                entity.BranchId = dto.BranchId;
                _uow.Repository<BillingCounter>().Update(entity);
            }
            else
            {
                entity = new BillingCounter
                {
                    Code = code,
                    Name = name,
                    IsDefault = dto.IsDefault,
                    Status = dto.Status,
                    BranchId = dto.BranchId
                };
                await _uow.Repository<BillingCounter>().AddAsync(entity, ct);
            }

            await _uow.SaveChangesAsync(ct);
            return Result.Success(entity.Id);
        }
        catch (Exception ex)
        {
            return Result.Failure<int>(ex.Message);
        }
    }

    public async Task EnsureDefaultCountersAsync(int? branchId, CancellationToken ct = default)
    {
        var exists = await _uow.Repository<BillingCounter>().Query()
            .AnyAsync(c => !c.IsDeleted && c.BranchId == branchId, ct);
        if (exists) return;

        await _uow.Repository<BillingCounter>().AddAsync(new BillingCounter
        {
            BranchId = branchId,
            Code = "C1",
            Name = "Counter 1",
            IsDefault = true,
            Status = EntityStatus.Active
        }, ct);
        await _uow.Repository<BillingCounter>().AddAsync(new BillingCounter
        {
            BranchId = branchId,
            Code = "C2",
            Name = "Counter 2",
            IsDefault = false,
            Status = EntityStatus.Active
        }, ct);
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<List<CounterPickDto>> ListForPickerAsync(int? branchId, CancellationToken ct = default)
    {
        await EnsureDefaultCountersAsync(branchId, ct);

        var counters = await _uow.Repository<BillingCounter>().Query().AsNoTracking()
            .Where(c => !c.IsDeleted && c.Status == EntityStatus.Active
                        && (!branchId.HasValue || c.BranchId == branchId))
            .OrderByDescending(c => c.IsDefault)
            .ThenBy(c => c.Code)
            .ToListAsync(ct);

        var open = await _uow.Repository<CounterSession>().Query().AsNoTracking()
            .Include(s => s.User)
            .Where(s => !s.IsDeleted && s.Status == CounterSessionStatus.Open)
            .ToListAsync(ct);

        return counters.Select(c =>
        {
            var session = open.FirstOrDefault(s => s.CounterId == c.Id);
            return new CounterPickDto(
                c.Id, c.Code, c.Name, c.IsDefault,
                session is not null, session?.User?.FullName, session?.UserId);
        }).ToList();
    }

    public async Task<Result<CounterSessionDto>> OpenSessionAsync(
        int counterId,
        int userId,
        decimal openingFloat,
        bool takeOverIfOpen = false,
        CancellationToken ct = default)
    {
        if (openingFloat < 0)
            return Result.Failure<CounterSessionDto>("Opening float cannot be negative.");

        var counter = await _uow.Repository<BillingCounter>().Query()
            .FirstOrDefaultAsync(c => c.Id == counterId && !c.IsDeleted, ct);
        if (counter is null || counter.Status != EntityStatus.Active)
            return Result.Failure<CounterSessionDto>("Counter not found or inactive.");

        var user = await _uow.Repository<User>().Query().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        if (user is null)
            return Result.Failure<CounterSessionDto>("User not found.");

        try
        {
            // Resume if this user already has an open session on this counter.
            var mine = await _uow.Repository<CounterSession>().Query()
                .Include(s => s.Counter)
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => !s.IsDeleted && s.Status == CounterSessionStatus.Open
                                          && s.CounterId == counterId && s.UserId == userId, ct);
            if (mine is not null)
                return Result.Success(MapSession(mine));

            var otherOnCounter = await _uow.Repository<CounterSession>().Query()
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => !s.IsDeleted && s.Status == CounterSessionStatus.Open
                                          && s.CounterId == counterId, ct);
            if (otherOnCounter is not null)
            {
                if (!takeOverIfOpen)
                {
                    return Result.Failure<CounterSessionDto>(
                        $"Counter {counter.Code} is already open by {otherOnCounter.User?.FullName ?? "another operator"}.");
                }

                // Stale / left-open session — close it so this operator can bill.
                var priorName = otherOnCounter.User?.FullName ?? $"User {otherOnCounter.UserId}";
                otherOnCounter.Status = CounterSessionStatus.Closed;
                otherOnCounter.ClosedAtUtc = _clock.UtcNow;
                otherOnCounter.Remarks = TruncateRemark(
                    $"Taken over by {user.FullName} (was open by {priorName}).");
                _uow.Repository<CounterSession>().Update(otherOnCounter);
            }

            // Close any other open session for this user (one counter at a time).
            var myOther = await _uow.Repository<CounterSession>().Query()
                .Where(s => !s.IsDeleted && s.Status == CounterSessionStatus.Open && s.UserId == userId)
                .ToListAsync(ct);
            foreach (var old in myOther)
            {
                old.Status = CounterSessionStatus.Closed;
                old.ClosedAtUtc = _clock.UtcNow;
                old.Remarks = "Auto-closed when opening another counter.";
                _uow.Repository<CounterSession>().Update(old);
            }

            // Switch-back: reopen today's auto-closed session on this counter so sales totals stay intact.
            var todayLocal = _clock.UtcNow.ToLocalTime().Date;
            var resumable = await _uow.Repository<CounterSession>().Query()
                .Include(s => s.Counter)
                .Include(s => s.User)
                .Where(s => !s.IsDeleted
                            && s.Status == CounterSessionStatus.Closed
                            && s.CounterId == counterId
                            && s.UserId == userId
                            && s.DeclaredClosingCash == null
                            && s.Remarks != null
                            && s.Remarks.StartsWith("Auto-closed when opening another counter"))
                .OrderByDescending(s => s.ClosedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (resumable is not null
                && resumable.ClosedAtUtc is DateTime closedAt
                && closedAt.ToLocalTime().Date == todayLocal)
            {
                resumable.Status = CounterSessionStatus.Open;
                resumable.ClosedAtUtc = null;
                resumable.Remarks = null;
                if (openingFloat > 0)
                    resumable.OpeningFloat = openingFloat;
                _uow.Repository<CounterSession>().Update(resumable);
                await _uow.SaveChangesAsync(ct);
                resumable.Counter ??= counter;
                resumable.User ??= user;
                return Result.Success(MapSession(resumable));
            }

            var session = new CounterSession
            {
                CounterId = counterId,
                UserId = userId,
                OpenedAtUtc = _clock.UtcNow,
                OpeningFloat = openingFloat,
                MachineName = Environment.MachineName,
                Status = CounterSessionStatus.Open
            };
            await _uow.Repository<CounterSession>().AddAsync(session, ct);
            await _uow.SaveChangesAsync(ct);

            session.Counter = counter;
            session.User = user;
            return Result.Success(MapSession(session));
        }
        catch (Exception ex)
        {
            return Result.Failure<CounterSessionDto>(ex.Message);
        }
    }

    public async Task<Result> CloseSessionAsync(
        int sessionId, decimal? declaredClosingCash, string? remarks, CancellationToken ct = default)
    {
        try
        {
            var session = await _uow.Repository<CounterSession>().Query()
                .FirstOrDefaultAsync(s => s.Id == sessionId && !s.IsDeleted, ct);
            if (session is null)
                return Result.Failure("Session not found.");
            if (session.Status != CounterSessionStatus.Open)
                return Result.Failure("Session is already closed.");

            session.Status = CounterSessionStatus.Closed;
            session.ClosedAtUtc = _clock.UtcNow;
            session.DeclaredClosingCash = declaredClosingCash;
            if (!string.IsNullOrWhiteSpace(remarks))
                session.Remarks = remarks.Trim();
            _uow.Repository<CounterSession>().Update(session);
            await _uow.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    public async Task<CounterSessionDto?> GetOpenSessionForUserAsync(int userId, CancellationToken ct = default)
    {
        var session = await _uow.Repository<CounterSession>().Query().AsNoTracking()
            .Include(s => s.Counter)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => !s.IsDeleted && s.Status == CounterSessionStatus.Open && s.UserId == userId, ct);
        return session is null ? null : MapSession(session);
    }

    public async Task<List<CounterCashSummaryDto>> GetCashSummaryAsync(
        int? branchId, DateTime businessDate, CancellationToken ct = default)
    {
        var dayStart = businessDate.Date;
        var dayEnd = dayStart.AddDays(1);

        var counters = await _uow.Repository<BillingCounter>().Query().AsNoTracking()
            .Where(c => !c.IsDeleted && c.Status == EntityStatus.Active
                        && (!branchId.HasValue || c.BranchId == branchId))
            .OrderBy(c => c.Code)
            .ToListAsync(ct);

        var result = new List<CounterCashSummaryDto>();
        foreach (var c in counters)
        {
            var row = await BuildCashRowAsync(c, null, dayStart, dayEnd, ct);
            result.Add(row);
        }

        return result;
    }

    public async Task<CounterCashSummaryDto?> GetActiveCounterCashAsync(
        int counterId, int? sessionId, DateTime businessDate, CancellationToken ct = default)
    {
        var counter = await _uow.Repository<BillingCounter>().Query().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == counterId && !c.IsDeleted, ct);
        if (counter is null) return null;

        var dayStart = businessDate.Date;
        var dayEnd = dayStart.AddDays(1);
        return await BuildCashRowAsync(counter, sessionId, dayStart, dayEnd, ct);
    }

    private async Task<CounterCashSummaryDto> BuildCashRowAsync(
        BillingCounter counter,
        int? preferSessionId,
        DateTime dayStart,
        DateTime dayEnd,
        CancellationToken ct)
    {
        var openSession = await _uow.Repository<CounterSession>().Query().AsNoTracking()
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => !s.IsDeleted && s.Status == CounterSessionStatus.Open
                                      && s.CounterId == counter.Id, ct);

        CounterSession? sessionRow = openSession;
        if (preferSessionId is int sidWanted)
        {
            sessionRow = await _uow.Repository<CounterSession>().Query().AsNoTracking()
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == sidWanted && !s.IsDeleted, ct)
                      ?? openSession;
        }

        var salesQ = _uow.Repository<Sale>().Query().AsNoTracking()
            .Include(s => s.Payments)
            .Where(s => !s.IsDeleted
                        && s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft);

        if (preferSessionId is int sid)
        {
            salesQ = salesQ.Where(s => s.CounterSessionId == sid);
        }
        else
        {
            salesQ = salesQ.Where(s => s.CounterId == counter.Id
                                       && s.InvoiceDate >= dayStart && s.InvoiceDate < dayEnd);
        }

        var sales = await salesQ.ToListAsync(ct);
        decimal cash = 0, card = 0, upi = 0, other = 0;
        foreach (var sale in sales)
        {
            foreach (var p in sale.Payments.Where(x => !x.IsDeleted))
            {
                switch (p.Method)
                {
                    case PaymentMethod.Cash: cash += p.Amount; break;
                    case PaymentMethod.Card: card += p.Amount; break;
                    case PaymentMethod.Upi: upi += p.Amount; break;
                    case PaymentMethod.Credit: break;
                    default: other += p.Amount; break;
                }
            }
        }

        var floatAmt = sessionRow?.OpeningFloat ?? 0m;
        return new CounterCashSummaryDto(
            counter.Id,
            counter.Code,
            counter.Name,
            sales.Count,
            cash,
            card,
            upi,
            other,
            floatAmt,
            floatAmt + cash,
            sessionRow?.User?.FullName,
            sessionRow?.Id);
    }

    private CounterSessionDto MapSession(CounterSession s) => new(
        s.Id,
        s.CounterId,
        s.Counter?.Code ?? $"#{s.CounterId}",
        s.Counter?.Name ?? $"Counter {s.CounterId}",
        s.UserId,
        s.User?.FullName ?? $"User {s.UserId}",
        s.OpenedAtUtc.ToLocalTime(),
        s.OpeningFloat);

    public async Task<Result<CounterDayCloseDto>> PreviewDayCloseAsync(int sessionId, CancellationToken ct = default)
    {
        var dto = await BuildDayCloseDtoAsync(sessionId, countedOverride: null, ct);
        if (dto is null)
            return Result.Failure<CounterDayCloseDto>("Counter session was not found.");
        if (dto.IsClosed)
            return Result.Failure<CounterDayCloseDto>("This counter session is already closed.");
        return Result.Success(dto);
    }

    public async Task<Result<CounterDayCloseDto>> CloseDayAsync(
        int sessionId, decimal countedCash, string? remarks, CancellationToken ct = default)
    {
        if (countedCash < 0)
            return Result.Failure<CounterDayCloseDto>("Counted cash cannot be negative.");

        var preview = await BuildDayCloseDtoAsync(sessionId, countedCash, ct);
        if (preview is null)
            return Result.Failure<CounterDayCloseDto>("Counter session was not found.");
        if (preview.IsClosed)
            return Result.Failure<CounterDayCloseDto>("This counter session is already closed.");

        var note = BuildCloseRemark(preview.ExpectedCashInDrawer, countedCash, remarks);
        var closed = await CloseSessionAsync(sessionId, countedCash, note, ct);
        if (closed.IsFailure)
            return Result.Failure<CounterDayCloseDto>(closed.Error ?? "Could not close the counter.");

        var report = await BuildDayCloseDtoAsync(sessionId, countedCash, ct);
        return report is null
            ? Result.Failure<CounterDayCloseDto>("Counter closed, but the report could not be loaded.")
            : Result.Success(report);
    }

    private async Task<CounterDayCloseDto?> BuildDayCloseDtoAsync(
        int sessionId, decimal? countedOverride, CancellationToken ct)
    {
        var session = await _uow.Repository<CounterSession>().Query().AsNoTracking()
            .Include(s => s.Counter)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == sessionId && !s.IsDeleted, ct);
        if (session is null) return null;

        var company = await _uow.Repository<CompanyProfile>().Query().AsNoTracking()
            .FirstOrDefaultAsync(ct);

        // Use the business day of this session (local), and include all sales for this
        // counter that day — not only CounterSessionId. Switching counters auto-closes and
        // can reopen a new session; earlier bills stay on the prior session id.
        var dayStart = session.OpenedAtUtc.ToLocalTime().Date;
        var dayEnd = dayStart.AddDays(1);
        var dayStartUtc = dayStart.ToUniversalTime();
        var dayEndUtc = dayEnd.ToUniversalTime();

        var todaysSessionIds = await _uow.Repository<CounterSession>().Query().AsNoTracking()
            .Where(s => !s.IsDeleted
                        && s.CounterId == session.CounterId
                        && s.OpenedAtUtc >= dayStartUtc && s.OpenedAtUtc < dayEndUtc)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (!todaysSessionIds.Contains(session.Id))
            todaysSessionIds.Add(session.Id);

        var sales = await _uow.Repository<Sale>().Query().AsNoTracking()
            .Include(s => s.Payments)
            .Where(s => !s.IsDeleted
                        && s.Status != SaleStatus.Cancelled
                        && s.Status != SaleStatus.Draft
                        && (
                            (s.CounterId == session.CounterId
                             && s.InvoiceDate >= dayStart && s.InvoiceDate < dayEnd)
                            || (s.CounterSessionId != null
                                && todaysSessionIds.Contains(s.CounterSessionId.Value))
                        ))
            .ToListAsync(ct);

        // Opening float: prefer earliest session opened that day for this user+counter
        // (switch-back may reopen a later empty session with a different float).
        var firstSessionToday = await _uow.Repository<CounterSession>().Query().AsNoTracking()
            .Where(s => !s.IsDeleted
                        && s.CounterId == session.CounterId
                        && s.UserId == session.UserId
                        && s.OpenedAtUtc >= dayStartUtc && s.OpenedAtUtc < dayEndUtc)
            .OrderBy(s => s.OpenedAtUtc)
            .FirstOrDefaultAsync(ct);
        var openingFloat = firstSessionToday?.OpeningFloat ?? session.OpeningFloat;

        decimal cash = 0, card = 0, upi = 0, other = 0, credit = 0;
        foreach (var sale in sales)
        {
            foreach (var p in sale.Payments.Where(x => !x.IsDeleted))
            {
                switch (p.Method)
                {
                    case PaymentMethod.Cash: cash += p.Amount; break;
                    case PaymentMethod.Card: card += p.Amount; break;
                    case PaymentMethod.Upi: upi += p.Amount; break;
                    case PaymentMethod.Credit: credit += p.Amount; break;
                    default: other += p.Amount; break;
                }
            }
        }

        var expected = Math.Round(openingFloat + cash, 2);
        var counted = countedOverride
                      ?? session.DeclaredClosingCash
                      ?? expected;

        return new CounterDayCloseDto
        {
            SessionId = session.Id,
            CompanyName = string.IsNullOrWhiteSpace(company?.CompanyName) ? "PharmaPOS" : company!.CompanyName,
            CounterCode = session.Counter?.Code ?? $"#{session.CounterId}",
            CounterName = session.Counter?.Name ?? $"Counter {session.CounterId}",
            OperatorName = session.User?.FullName ?? $"User {session.UserId}",
            OpenedAtLocal = (firstSessionToday ?? session).OpenedAtUtc.ToLocalTime(),
            ClosedAtLocal = session.ClosedAtUtc?.ToLocalTime(),
            OpeningFloat = openingFloat,
            BillCount = sales.Count,
            CashCollected = cash,
            CardCollected = card,
            UpiCollected = upi,
            OtherCollected = other,
            CreditCollected = credit,
            ExpectedCashInDrawer = expected,
            CountedCash = counted,
            Remarks = session.Remarks,
            MachineName = session.MachineName,
            IsClosed = session.Status == CounterSessionStatus.Closed
        };
    }

    private static string BuildCloseRemark(decimal expected, decimal counted, string? remarks)
    {
        var variance = Math.Round(counted - expected, 2);
        var label = variance > 0.009m ? "excess" : variance < -0.009m ? "shortage" : "matched";
        var auto = $"Counted ₹{counted:N2}; system ₹{expected:N2}; {label} ₹{Math.Abs(variance):N2}.";
        if (string.IsNullOrWhiteSpace(remarks))
            return TruncateRemark(auto);
        return TruncateRemark(auto + " " + remarks.Trim());
    }

    private static string TruncateRemark(string text)
        => text.Length <= 500 ? text : text[..500];
}
