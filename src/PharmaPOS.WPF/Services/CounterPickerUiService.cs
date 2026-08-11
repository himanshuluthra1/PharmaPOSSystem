using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.WPF.ViewModels;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

/// <summary>Shows the billing-counter picker (login or change-counter).</summary>
public interface ICounterPickerUiService
{
    /// <param name="switchMode">
    /// When true, skips auto-resume and lets the operator move to another counter.
    /// </param>
    bool ShowPicker(bool switchMode = false);
}

public sealed class CounterPickerUiService : ICounterPickerUiService
{
    private readonly IServiceProvider _services;

    public CounterPickerUiService(IServiceProvider services)
    {
        _services = services;
    }

    public bool ShowPicker(bool switchMode = false)
    {
        var vm = _services.GetRequiredService<CounterSelectViewModel>();
        vm.IsSwitchMode = switchMode;
        var window = _services.GetRequiredService<CounterSelectWindow>();
        window.DataContext = vm;

        // After login the closed LoginWindow may still be MainWindow, or the
        // picker itself can become MainWindow — never set Owner to itself.
        var owner = System.Windows.Application.Current?.Windows
            .OfType<System.Windows.Window>()
            .FirstOrDefault(w =>
                !ReferenceEquals(w, window)
                && w.IsVisible
                && w.IsLoaded);
        if (owner is not null)
            window.Owner = owner;

        return window.ShowDialog() == true
               && _services.GetRequiredService<ICounterContextService>().HasActiveCounter;
    }
}
