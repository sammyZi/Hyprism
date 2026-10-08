using Hyprism.Core;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Hyprism.App;

public partial class App : Application
{
    public static MainWindow Window { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Keep the tray app alive; surface the error instead of crashing the session's glue.
            e.Handled = true;
            Window?.Toast("Something went wrong", e.Exception.Message);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        Store.Load();
        Hub.Init(Modules.Catalog.All);
        Window = new MainWindow();

        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (!args.Contains("--tray")) { Window.Activate(); Window.Maximize(); }
        _ = HandleArgsAsync(args);

        AppInstance.GetCurrent().Activated += (_, a) =>
        {
            var line = (a.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs)?.Arguments ?? "";
            Window.DispatcherQueue.TryEnqueue(async () =>
            {
                var redirected = line.Split(' ', StringSplitOptions.RemoveEmptyEntries); // may start with the exe path; only flags matter
                if (!redirected.Contains("--theme")) Window.ShowFromTray();
                await HandleArgsAsync(redirected);
            });
        };
    }

    /// <summary>`--theme light|dark` comes from Auto Dark Mode's script hook.</summary>
    static async Task HandleArgsAsync(string[] args)
    {
        var i = Array.IndexOf(args, "--theme");
        if (i >= 0 && i + 1 < args.Length)
        {
            try { await Hub.OnSystemThemeAsync(args[i + 1].Equals("dark", StringComparison.OrdinalIgnoreCase)); }
            catch (Exception ex) { Window.Toast("Theme switch failed", ex.Message); }
        }
    }
}
