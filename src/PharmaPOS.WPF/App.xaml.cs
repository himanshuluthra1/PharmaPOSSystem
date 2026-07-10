using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaPOS.Application;
using PharmaPOS.Infrastructure;
using PharmaPOS.Persistence;
using PharmaPOS.Persistence.Seed;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.ViewModels;
using PharmaPOS.WPF.ViewModels.Sales;
using PharmaPOS.WPF.ViewModels.Purchases;
using PharmaPOS.WPF.ViewModels.Masters;
using PharmaPOS.WPF.ViewModels.Inventory;
using PharmaPOS.WPF.ViewModels.Accounting;
using PharmaPOS.WPF.ViewModels.Reports;
using PharmaPOS.WPF.ViewModels.Settings;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF;

/// <summary>
/// Application entry point. Configures the generic host / DI container, ensures
/// the database is created and seeded, then drives the login -> shell -> logout
/// lifecycle.
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddApplication();
                services.AddInfrastructure();
                services.AddPersistence(context.Configuration);
                RegisterPresentation(services);
            })
            .Build();
    }

    public static IServiceProvider Services { get; private set; } = default!;

    private static void RegisterPresentation(IServiceCollection services)
    {
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IInvoicePrintService, InvoicePrintService>();
        services.AddSingleton<IMedicinePickerService, MedicinePickerService>();
        services.AddSingleton<IBillSearchService, BillSearchService>();
        services.AddSingleton<IPurchaseSearchService, PurchaseSearchService>();

        // View models (transient so each navigation gets fresh data/context).
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<SalesViewModel>();
        services.AddTransient<PurchaseViewModel>();
        services.AddTransient<InventoryViewModel>();
        services.AddTransient<MastersViewModel>();
        services.AddTransient<AccountingViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddTransient<LoginWindow>();
        services.AddTransient<MainWindow>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await _host.StartAsync();
        Services = _host.Services;

        // We drive login -> shell -> logout ourselves, so opening/closing windows
        // in between must not auto-terminate the app.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Unexpected error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        await InitializeDatabaseAsync();
        ShowLogin();
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            var seeder = Services.GetRequiredService<DbSeeder>();
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "The application could not connect to or initialize the database.\n\n" +
                $"{ex.Message}\n\nUpdate the connection string in appsettings.json and restart.",
                "Database initialization failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowLogin()
    {
        var loginWindow = Services.GetRequiredService<LoginWindow>();
        var loginVm = Services.GetRequiredService<LoginViewModel>();
        loginWindow.DataContext = loginVm;

        loginVm.LoginSucceeded += () =>
        {
            loginWindow.Tag = "success";
            loginWindow.Close();
        };

        loginWindow.Closed += (_, _) =>
        {
            if (loginWindow.Tag as string == "success")
                ShowShell();
            else
                Shutdown();
        };

        loginWindow.Show();
    }

    private void ShowShell()
    {
        var mainWindow = Services.GetRequiredService<MainWindow>();
        var mainVm = Services.GetRequiredService<MainViewModel>();
        mainWindow.DataContext = mainVm;

        mainVm.LogoutRequested += () =>
        {
            // Flag the close as a logout so it re-opens login instead of exiting.
            mainWindow.Tag = "logout";
            mainWindow.Close();
        };

        mainWindow.Closed += (_, _) =>
        {
            if (mainWindow.Tag as string == "logout")
                ShowLogin();
            else
                Shutdown();
        };

        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
