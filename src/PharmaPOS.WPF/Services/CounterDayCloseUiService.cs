using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.WPF.ViewModels;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

public interface ICounterDayCloseUiService
{
    bool ShowForActiveSession();
}

public sealed class CounterDayCloseUiService : ICounterDayCloseUiService
{
    private readonly IServiceProvider _services;

    public CounterDayCloseUiService(IServiceProvider services)
    {
        _services = services;
    }

    public bool ShowForActiveSession()
    {
        var context = _services.GetRequiredService<ICounterContextService>();
        if (context.ActiveSessionId is not int sessionId)
            return false;

        var vm = _services.GetRequiredService<CounterDayCloseViewModel>();
        var window = _services.GetRequiredService<CounterDayCloseWindow>();
        window.DataContext = vm;

        var owner = System.Windows.Application.Current?.Windows
            .OfType<System.Windows.Window>()
            .FirstOrDefault(w =>
                !ReferenceEquals(w, window)
                && w.IsVisible
                && w.IsLoaded);
        if (owner is not null)
            window.Owner = owner;

        window.Loaded += async (_, _) => await vm.LoadAsync(sessionId);
        return window.ShowDialog() == true;
    }
}
