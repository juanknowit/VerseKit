using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using VerseKit.App.Services;
using VerseKit.App.Theming;
using VerseKit.App.ViewModels;
using VerseKit.App.Views;
using VerseKit.Core.Services;

namespace VerseKit.App;

public partial class App : Application
{
    private IServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _services = BuildServices();

        // Apply the saved accent + background before the window renders so the
        // first frame already uses the user's chosen colour and surface.
        // Accent first: the Theme background derives its gradient from it.
        ThemeManager.Apply(ThemeManager.LoadSavedPreset());
        ThemeManager.ApplyBackground(ThemeManager.LoadSavedBackground());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = _services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };

            desktop.Startup += async (_, _) =>
            {
                await vm.LoadPluginsAsync(CancellationToken.None);
                _ = vm.CheckForUpdatesAsync(CancellationToken.None); // fire-and-forget
            };

            desktop.Exit += (_, _) =>
            {
                (_services as IDisposable)?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider BuildServices()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "versekit", "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(logDir, "versekit-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        var services = new ServiceCollection();

        services.AddLogging(b => b.AddSerilog(dispose: true));

        // Use FileSecretStore in debug builds; swap to KeychainService in production.
#if DEBUG
        services.AddSingleton<ISecretStore, FileSecretStore>();
#else
        services.AddSingleton<ISecretStore, KeychainService>();
#endif

        services.AddSingleton<DataverseClientFactory>();
        services.AddSingleton<ConnectionManager>();
        services.AddSingleton<PluginHost>();

        services.AddSingleton<UpdateService>();
        services.AddSingleton<ConnectionViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
