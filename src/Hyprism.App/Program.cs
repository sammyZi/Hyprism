using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Hyprism.App;

/// <summary>
/// Custom entry point for single instance: a second launch (e.g. Auto Dark Mode running `Hyprism.exe --theme dark`)
/// hands its arguments to the running instance and exits.
/// </summary>
public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Headless uninstall cleanup, called by the uninstaller before it deletes files.
        //   Hyprism.exe --uninstall [--restore] [--purge] [--dry-run]
        if (args.Contains("--uninstall")) return RunCleanup(args);

        WinRT.ComWrappersSupport.InitializeComWrappers();
        var main = AppInstance.FindOrRegisterForKey("Hyprism");
        if (!main.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            main.RedirectActivationToAsync(activation).AsTask().Wait();
            return 0;
        }
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    static int RunCleanup(string[] args)
    {
        var logFile = Path.Combine(Path.GetTempPath(), "hyprism-uninstall.log");
        try
        {
            var lines = Hyprism.Core.Cleanup.RunAsync(Modules.Catalog.All, args.Contains("--restore"), args.Contains("--purge"), args.Contains("--dry-run"))
                .GetAwaiter().GetResult();
            File.WriteAllLines(logFile, lines.Prepend($"Hyprism cleanup {DateTime.Now:u}  args: {string.Join(' ', args)}"));
            return 0;
        }
        catch (Exception e)
        {
            File.WriteAllText(logFile, e.ToString());
            return 1;
        }
    }
}
