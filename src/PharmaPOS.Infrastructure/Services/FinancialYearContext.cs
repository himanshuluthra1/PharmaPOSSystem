using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Shared;

namespace PharmaPOS.Infrastructure.Services;

/// <summary>Caches the shop's selected view FY from Company Preferences.</summary>
public sealed class FinancialYearContext : IFinancialYearContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly object _gate = new();
    private FinancialYearInfo _active;

    public FinancialYearContext(IServiceScopeFactory scopeFactory, IDateTimeProvider clock)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _active = FinancialYearHelper.FromLocalDate(_clock.Today);
    }

    public FinancialYearInfo Active
    {
        get { lock (_gate) return _active; }
    }

    public bool CanEditTransactions => Active.IsCurrent;

    public event Action? Changed;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        int? viewYear = null;
        using (var scope = _scopeFactory.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            var prefs = await settings.GetPreferencesAsync(ct).ConfigureAwait(false);
            viewYear = prefs.ViewFinancialYearStartYear;
        }

        var next = FinancialYearHelper.Resolve(viewYear, _clock.Today);
        lock (_gate)
            _active = next;

        Changed?.Invoke();
    }
}
