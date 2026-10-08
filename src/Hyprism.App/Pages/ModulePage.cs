using System.Text.Json.Nodes;
using Hyprism.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Hyprism.App.Pages;

/// <summary>One page for every module: status, lifecycle buttons, then its settings rendered generically.</summary>
public sealed class ModulePage : PageBase
{
    IModule module = null!;
    JsonObject working = [];
    readonly TextBlock statusText = Ui.Text("Checking…", "MutedStyle");
    readonly Ellipse dot = new() { Width = 8, Height = 8 };
    readonly Button installButton = new() { Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    readonly ToggleSwitch enabledSwitch = new() { OnContent = "On", OffContent = "Off", MinWidth = 0 };
    readonly InfoBar busyBar = new() { IsClosable = false, Severity = InfoBarSeverity.Informational };
    readonly TextBlock log = new() { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    readonly Button applyButton = new() { Content = "Apply", Style = (Style)Application.Current.Resources["AccentButtonStyle"], IsEnabled = false };
    ModuleStatus status = ModuleStatus.Missing;
    bool suppressToggle;

    public ModulePage(string id) : base("")
    {
        module = Hub.Get(id)!;
        working = module.WithDefaults(Store.Config.For(module.Id).Settings);
        BuildHeader();
        BuildBody();
        Loaded += async (_, _) => await RefreshStatusAsync();
    }

    void BuildHeader()
    {
        Header.Children.Clear();
        var tile = new Border
        {
            Width = 56, Height = 56, CornerRadius = new CornerRadius(14),
            Background = Ui.Res("AccentFillColorDefaultBrush"),
            Child = new FontIcon { Glyph = module.Glyph, FontSize = 26, Foreground = Ui.Res("TextOnAccentFillColorPrimaryBrush") },
        };
        var link = new HyperlinkButton { Content = "github.com/" + module.Repo, NavigateUri = new Uri("https://github.com/" + module.Repo), Padding = new Thickness(0) };
        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { link, Ui.Text("·", "MutedStyle"), Ui.Text(module.License, "MutedStyle") },
        };
        foreach (var c in meta.Children.OfType<TextBlock>()) c.VerticalAlignment = VerticalAlignment.Center;
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { dot, statusText } };
        dot.VerticalAlignment = VerticalAlignment.Center;
        var titles = new StackPanel
        {
            Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
            Children = { Ui.Text(module.Name, "PageTitleStyle"), Ui.Text(module.Description, "MutedStyle", 14), meta, statusRow },
        };

        var more = new DropDownButton { Content = Ui.Icon("", 14) };
        var flyout = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        var restore = new MenuFlyoutItem { Text = "Restore original config", Icon = new FontIcon { Glyph = "" } };
        restore.Click += async (_, _) =>
        {
            if (!await Confirm("Restore original config?", $"Puts {module.Name}'s config files back exactly as they were before Hyprism changed them.", "Restore")) return;
            await Run("Restoring", ct => module.RestoreDefaultsAsync(ct));
            working = module.WithDefaults([]);
            BuildBody();
        };
        var uninstall = new MenuFlyoutItem { Text = "Uninstall", Icon = new FontIcon { Glyph = "" } };
        uninstall.Click += async (_, _) =>
        {
            if (!await Confirm($"Uninstall {module.Name}?", "Hyprism stops it and removes it with winget (or deletes its folder).", "Uninstall")) return;
            var progress = Progress();
            await Run("Uninstalling", ct => module.UninstallAsync(progress, ct));
        };
        flyout.Items.Add(restore);
        flyout.Items.Add(uninstall);
        more.Flyout = flyout;

        // Lambdas passed to Run execute on the thread pool: read UI state before, not inside them.
        installButton.Click += async (_, _) =>
        {
            var progress = Progress();
            bool wasInstalled = status.Installed;
            var settings = (JsonObject)working.DeepClone();
            await Run(wasInstalled ? "Updating" : "Installing", async ct =>
            {
                await module.InstallAsync(progress, ct);
                if (!wasInstalled) Store.Config.For(module.Id).Settings = settings;
            });
        };
        enabledSwitch.Toggled += async (_, _) =>
        {
            if (suppressToggle) return;
            bool on = enabledSwitch.IsOn;
            var settings = (JsonObject)working.DeepClone();
            await Run(on ? "Starting" : "Stopping", async ct =>
            {
                if (on) { await module.EnableAsync(ct); await module.ApplyAsync(settings, ct); }
                else await module.DisableAsync(ct);
            });
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top, Children = { enabledSwitch, installButton, more } };
        var g = new Grid { ColumnSpacing = 20 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tile.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(titles, 1); Grid.SetColumn(actions, 2);
        g.Children.Add(tile); g.Children.Add(titles); g.Children.Add(actions);
        Header.Children.Add(g);

        BuildProgressCard();
        Header.Children.Add(progressCard);
        busyBar.IsOpen = false;
        busyBar.Content = new Expander { Header = "Details", Content = new ScrollViewer { Content = log, MaxHeight = 200 }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Header.Children.Add(busyBar);
    }

    // ---------- live progress ----------

    readonly Border progressCard = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    readonly TextBlock progressTitle = new() { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    readonly TextBlock elapsedText = Ui.Text("0:00", "MutedStyle", 12);
    readonly TextBlock stageText = Ui.Text("Starting…", "MutedStyle", 13);
    readonly ProgressBar progressBar = new() { IsIndeterminate = true, Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBlock hintText = Ui.Text("", "MutedStyle", 12);
    readonly Button cancelButton = new() { Content = "Cancel" };
    readonly TextBlock liveLog = new() { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    CancellationTokenSource? runCts;
    DateTime runStarted, lastOutput;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? ticker;

    void BuildProgressCard()
    {
        cancelButton.Click += (_, _) => { runCts?.Cancel(); stageText.Text = "Cancelling…"; cancelButton.IsEnabled = false; };
        hintText.Foreground = Ui.Res("SystemFillColorCautionBrush");
        var top = new Grid { ColumnSpacing = 12 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ring = new ProgressRing { IsActive = true, Width = 16, Height = 16 };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { ring, progressTitle } };
        elapsedText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(elapsedText, 1); Grid.SetColumn(cancelButton, 2);
        top.Children.Add(title); top.Children.Add(elapsedText); top.Children.Add(cancelButton);
        var details = new Expander
        {
            Header = "Details", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new ScrollViewer { Content = liveLog, MaxHeight = 200 },
        };
        progressCard.Style = Ui.Style("CardStyle");
        progressCard.Child = new StackPanel { Spacing = 8, Children = { top, stageText, progressBar, hintText, details } };
    }

    /// <summary>winget's raw lines, as a sentence a person understands.</summary>
    static string Friendly(string line)
    {
        var l = new string(line.Where(c => c is not ('█' or '▒')).ToArray()).Trim();
        if (Sys.ParseProgress(l) is { } f && l.Contains('/')) return $"Downloading  {l}";
        return l switch
        {
            _ when l.StartsWith("winget install", StringComparison.OrdinalIgnoreCase) => "Asking winget for the package…",
            _ when l.StartsWith("winget uninstall", StringComparison.OrdinalIgnoreCase) => "Asking winget to uninstall…",
            _ when l.StartsWith("Found ", StringComparison.Ordinal) => l,
            _ when l.StartsWith("Downloading http", StringComparison.OrdinalIgnoreCase) => "Downloading the installer…",
            _ when l.Contains("verified installer hash", StringComparison.OrdinalIgnoreCase) => "Verified the download",
            _ when l.Contains("Starting package install", StringComparison.OrdinalIgnoreCase) => "Running the app's installer…",
            _ when l.Contains("Starting package uninstall", StringComparison.OrdinalIgnoreCase) => "Running the app's uninstaller…",
            _ when l.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase) => "Installed",
            _ when l.Contains("Successfully uninstalled", StringComparison.OrdinalIgnoreCase) => "Uninstalled",
            _ when l.Contains("license", StringComparison.OrdinalIgnoreCase) || l.Contains("Microsoft is not responsible", StringComparison.OrdinalIgnoreCase) => "Accepting the package agreements…",
            _ => l.Length > 140 ? l[..140] + "…" : l,
        };
    }

    void OnTick()
    {
        var elapsed = DateTime.UtcNow - runStarted;
        elapsedText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        var quiet = DateTime.UtcNow - lastOutput;
        bool installerRunning = stageText.Text.StartsWith("Running the app's", StringComparison.Ordinal);
        hintText.Text = installerRunning && quiet > TimeSpan.FromSeconds(12)
            ? "Still running. Some installers ask for administrator permission: if a Windows prompt opened, it may be behind this window or flashing in the taskbar."
            : quiet > TimeSpan.FromSeconds(45)
                ? "No news from winget for a while. It's probably still working (large downloads and first-time setup can be slow). You can cancel and try again."
                : "";
        hintText.Visibility = hintText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void BuildBody()
    {
        Body.Children.Clear();
        if (module.Settings.Count == 0)
        {
            Body.Children.Add(Ui.Card(Ui.Text("Nothing to configure here: Hyprism installs it, keeps it updated, and starts it with Windows.", "MutedStyle")));
            return;
        }
        SettingsRenderer.Render(module, working, Body, () => applyButton.IsEnabled = true,
            key => Run("Working", ct => module.InvokeAsync(key, ct)));

        applyButton.Click -= Apply;
        applyButton.Click += Apply;
        var revert = new Button { Content = "Discard changes" };
        revert.Click += (_, _) => { working = module.WithDefaults(Store.Config.For(module.Id).Settings); applyButton.IsEnabled = false; BuildBody(); };
        Body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 24, 0, 0),
            Children = { applyButton, revert },
        });
    }

    async void Apply(object sender, RoutedEventArgs e)
    {
        var settings = (JsonObject)working.DeepClone();
        bool installed = status.Installed;
        applyButton.IsEnabled = false;
        bool ok = await Run("Applying", async ct =>
        {
            Store.Config.For(module.Id).Settings = (JsonObject)settings.DeepClone();
            Store.Save();
            if (installed) await module.ApplyAsync(settings, ct);
        });
        if (ok) Main.Toast($"{module.Name} updated", installed ? null : "Saved. It will be applied once the app is installed.", InfoBarSeverity.Success);
        else applyButton.IsEnabled = true;
    }

    /// <summary>Live output from winget / downloads: updates the stage line, the progress bar and the details log.</summary>
    IProgress<string> Progress() => new Progress<string>(line =>
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lastOutput = DateTime.UtcNow;
        var friendly = Friendly(line.Trim());
        stageText.Text = friendly;
        if (Sys.ParseProgress(line) is { } fraction)
        {
            progressBar.IsIndeterminate = false;
            progressBar.Value = fraction * 100;
        }
        else if (friendly is not "Installed" and not "Uninstalled") progressBar.IsIndeterminate = true;
        // Progress-bar redraws only update the stage line; real messages go to the log.
        if (Sys.ParseProgress(line) is null)
        {
            liveLog.Text = (liveLog.Text + "\n" + line.Trim()).Trim();
            log.Text = liveLog.Text;
        }
    });

    /// <summary>
    /// Runs module work on the thread pool (so the UI keeps rendering every frame) with a live progress card:
    /// current stage, real download progress, elapsed time, hints when it's waiting on Windows, and Cancel.
    /// </summary>
    async Task<bool> Run(string what, Func<CancellationToken, Task> action)
    {
        runCts?.Cancel();
        var cts = runCts = new CancellationTokenSource();
        progressTitle.Text = $"{what} {module.Name}…";
        stageText.Text = "Starting…";
        liveLog.Text = log.Text = "";
        hintText.Visibility = Visibility.Collapsed;
        progressBar.IsIndeterminate = true;
        cancelButton.IsEnabled = true;
        cancelButton.Visibility = what is "Installing" or "Updating" or "Uninstalling" or "Working" ? Visibility.Visible : Visibility.Collapsed;
        busyBar.IsOpen = false;
        progressCard.Visibility = Visibility.Visible;
        runStarted = lastOutput = DateTime.UtcNow;
        ticker ??= DispatcherQueue.CreateTimer();
        ticker.Interval = TimeSpan.FromSeconds(1);
        ticker.Tick -= Tick; ticker.Tick += Tick;
        ticker.Start();
        installButton.IsEnabled = enabledSwitch.IsEnabled = false;
        bool ok = false;
        try
        {
            await Task.Run(() => action(cts.Token), cts.Token);
            ok = true;
            ShowResult(InfoBarSeverity.Success, $"{what} {module.Name}: done", null);
        }
        catch (OperationCanceledException) { ShowResult(InfoBarSeverity.Warning, $"{what} {module.Name}: cancelled", "Nothing else was changed. You can try again any time."); }
        catch (Exception ex) { ShowResult(InfoBarSeverity.Error, $"{what} {module.Name} failed", ex.Message); }
        finally
        {
            ticker.Stop();
            progressCard.Visibility = Visibility.Collapsed;
            installButton.IsEnabled = enabledSwitch.IsEnabled = true;
            await RefreshStatusAsync();
        }
        return ok;
    }

    void Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => OnTick();

    void ShowResult(InfoBarSeverity severity, string title, string? message)
    {
        var elapsed = DateTime.UtcNow - runStarted;
        busyBar.Title = title;
        busyBar.Message = (message is null ? "" : message + "  ") + $"({(int)elapsed.TotalMinutes}:{elapsed.Seconds:00})";
        busyBar.Severity = severity;
        busyBar.IsClosable = true;
        // Keep the log reachable after the card closes; quick successful actions don't need a banner.
        busyBar.IsOpen = severity != InfoBarSeverity.Success || liveLog.Text.Length > 0;
    }

    async Task RefreshStatusAsync()
    {
        statusText.Text = "Checking…";
        dot.Fill = Ui.Res("TextFillColorTertiaryBrush");
        try { status = await Task.Run(() => module.GetStatusAsync()); }
        catch { status = ModuleStatus.Missing; }

        var parts = new List<string>();
        if (!status.Installed) parts.Add("Not installed");
        else
        {
            bool managed = Store.Config.For(module.Id).Enabled;
            parts.Add(module.RunsInBackground ? (status.Running ? "Running" : "Installed, not running") : (managed ? "Themed by Hyprism" : "Installed"));
            if (status.Version is { } v) parts.Add("v" + v.TrimStart('v'));
            if (status.UpdateAvailable) parts.Add($"update to {status.Available} available");
        }
        statusText.Text = string.Join("  ·  ", parts);
        bool healthy = module.RunsInBackground ? status.Running : Store.Config.For(module.Id).Enabled;
        dot.Fill = Ui.Res(!status.Installed ? "TextFillColorTertiaryBrush" : status.UpdateAvailable ? "SystemFillColorCautionBrush" : healthy ? "SystemFillColorSuccessBrush" : "SystemFillColorNeutralBrush");
        ToolTipService.SetToolTip(enabledSwitch, module.RunsInBackground ? "Run now and at sign-in" : "Keep it themed by Hyprism");
        installButton.Content = !status.Installed ? "Install" : status.UpdateAvailable ? "Update" : "Reinstall";
        installButton.Style = (Style)Application.Current.Resources[!status.Installed || status.UpdateAvailable ? "AccentButtonStyle" : "DefaultButtonStyle"];
        enabledSwitch.Visibility = status.Installed ? Visibility.Visible : Visibility.Collapsed;
        suppressToggle = true;
        enabledSwitch.IsOn = Store.Config.For(module.Id).Enabled || (module.RunsInBackground && status.Running);
        suppressToggle = false;
    }
}
