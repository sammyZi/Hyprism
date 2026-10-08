using Hyprism.Core;

namespace Hyprism.Modules;

public static class Catalog
{
    /// <summary>Every module, in the order the UI lists them.</summary>
    public static IReadOnlyList<IModule> All { get; } =
    [
        // Look
        new TranslucentTB(), new WindowsTerminal(), new ExplorerBlurMica(), new MicaForEveryone(),
        new NilesoftShell(), new FluentFlyout(), new AutoDarkMode(), new Windhawk(),
        // Desktop
        new Lively(), new Tiling(), new StatusBar(), new TrafficMonitor(),
        // Productivity
        new FlowLauncher(), new PowerToys(), new Espanso(), new ShareX(), new EarTrumpet(),
        new TwinkleTray(), new Ditto(), new QuickLook(), new FilesApp(),
    ];
}
