using CommunityToolkit.WinUI.Controls;
using Hyprism.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hyprism.App.Pages;

public sealed class SettingsPage : PageBase
{
    public SettingsPage() : base("Settings")
    {
        var c = Store.Config;

        Section("General");
        var startup = new ToggleSwitch { IsOn = Sys.HasAutostart("Hyprism") };
        startup.Toggled += (_, _) =>
        {
            Sys.SetAutostart("Hyprism", startup.IsOn ? $"\"{Environment.ProcessPath}\" --tray" : null);
            c.StartWithWindows = startup.IsOn; Store.Save();
        };
        var theme = new ComboBox { ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = c.AppTheme, MinWidth = 140 };
        theme.SelectionChanged += (_, _) => { c.AppTheme = (string)theme.SelectedItem; Store.Save(); Main.ApplyAppTheme(); };
        Cards(
            new SettingsCard { Header = "Start with Windows", Description = "Starts in the tray and re-applies your setup at sign-in.", HeaderIcon = Ui.Icon(""), Content = startup },
            new SettingsCard { Header = "App theme", HeaderIcon = Ui.Icon(""), Content = theme });

        Section("Hotkeys");
        var labels = new Dictionary<string, string>
        {
            ["switchProfile"] = "Next profile", ["nextWallpaper"] = "Next wallpaper", ["toggleTiling"] = "Toggle tiling",
            ["toggleBlur"] = "Toggle blur", ["performance"] = "Performance mode",
        };
        var hotkeyCards = new List<UIElement>();
        foreach (var (key, label) in labels)
        {
            var recorder = Ui.ShortcutRecorder(c.Hotkeys.GetValueOrDefault(key, ""), s => { c.Hotkeys[key] = s; Store.Save(); Main.RegisterHotkeys(); });
            hotkeyCards.Add(new SettingsCard { Header = label, Content = recorder });
        }
        Cards([.. hotkeyCards]);

        Section("Automation");
        var perf = new ToggleSwitch { IsOn = c.AutoPerformanceMode };
        perf.Toggled += (_, _) => { c.AutoPerformanceMode = perf.IsOn; Store.Save(); };
        var safe = new ToggleSwitch { IsOn = c.SafeMode };
        safe.Toggled += (_, _) => { c.SafeMode = safe.IsOn; Store.Save(); };
        Cards(
            new SettingsCard { Header = "Automatic performance mode", Description = "On battery or while a fullscreen game runs: pause live wallpapers and turn blur off.", HeaderIcon = Ui.Icon(""), Content = perf },
            new SettingsCard { Header = "Safe mode", Description = "If Explorer crashes repeatedly after a change, undo it and tell you.", HeaderIcon = Ui.Icon(""), Content = safe });

        Section("Backup");
        var backup = new Button { Content = WindowsLook.HasOriginalBackup ? "Saved" : "Back up now", IsEnabled = !WindowsLook.HasOriginalBackup };
        backup.Click += async (_, _) => await Try("Backup", async () => { await Task.Run(WindowsLook.BackupOriginalAsync); backup.Content = "Saved"; backup.IsEnabled = false; });
        var restore = new Button { Content = "Restore…" };
        restore.Click += async (_, _) =>
        {
            if (!await Confirm("Restore the original Windows look?",
                "Hyprism stops every module it manages, puts back every config file it changed, and restores your original wallpaper, colors and theme. Installed apps stay installed.", "Restore everything")) return;
            await Try("Restore", async () =>
            {
                await Task.Run(async () =>
                {
                    foreach (var m in Hub.Modules.Where(m => Store.Config.For(m.Id).Enabled))
                        try { await m.DisableAsync(); } catch { /* keep going: restoring matters more */ }
                    foreach (var id in Backup.ModulesWithBackups()) Backup.RestoreOriginal(id);
                    await WindowsLook.RestoreOriginalAsync();
                    await Sys.RestartExplorerAsync();
                });
                Main.Toast("Original look restored", null, InfoBarSeverity.Success);
            });
        };
        var folder = new Button { Content = "Open" };
        folder.Click += (_, _) => Sys.OpenUrl(Store.Root);
        Cards(
            new SettingsCard { Header = "Original Windows look", Description = "Wallpaper, accent, light/dark and transparency as they were before Hyprism.", HeaderIcon = Ui.Icon(""), Content = backup },
            new SettingsCard { Header = "Restore original look", Description = "Undo everything Hyprism changed.", HeaderIcon = Ui.Icon(""), Content = restore },
            new SettingsCard { Header = "Settings folder", Description = Store.Root, HeaderIcon = Ui.Icon(""), Content = folder });


        Section("Updates");
        var repo = new TextBox { Text = c.UpdateRepo ?? "", PlaceholderText = "owner/repo", Width = 220 };
        repo.LostFocus += (_, _) => { c.UpdateRepo = repo.Text.Trim(); Store.Save(); };
        var auto = new ToggleSwitch { IsOn = c.AutoCheckUpdates };
        auto.Toggled += (_, _) => { c.AutoCheckUpdates = auto.IsOn; Store.Save(); };
        var status = new Ui.StatusPill();
        status.Set(Ui.Status.Waiting, "v" + Updater.Current.ToString(3));
        var check = new Button { Content = "Check now" };
        check.Click += async (_, _) =>
        {
            c.UpdateRepo = repo.Text.Trim(); Store.Save();
            if (string.IsNullOrEmpty(c.UpdateRepo)) { status.Set(Ui.Status.Warning, "Set an update source first"); return; }
            check.IsEnabled = false;
            status.Set(Ui.Status.Working, "Checking…");
            try
            {
                var update = await Updater.CheckAsync();
                if (update is null) { status.Set(Ui.Status.Done, $"Up to date (v{Updater.Current.ToString(3)})"); return; }
                status.Set(Ui.Status.Warning, $"{update.Tag} available");
                if (!await Confirm($"Install Hyprism {update.Tag}?", "Hyprism downloads the verified installer, closes, updates in place and starts again. Your settings and profiles are kept.", "Update now")) return;
                status.Set(Ui.Status.Working, "Downloading…");
                await Updater.InstallAsync(update, new Progress<double>(f => status.Set(Ui.Status.Working, $"Downloading… {f:P0}")));
                status.Set(Ui.Status.Working, "Installing…"); // the setup closes Hyprism from here
            }
            catch (Exception ex) { status.Set(Ui.Status.Failed, "Update failed: " + ex.Message); }
            finally { check.IsEnabled = true; }
        };
        Cards(
            new SettingsCard { Header = "Update source", Description = "GitHub repository whose Releases publish Hyprism-Setup-<version>-x64.exe.", HeaderIcon = Ui.Icon(""), Content = repo },
            new SettingsCard { Header = "Check for updates automatically", Description = "Once a day at startup.", HeaderIcon = Ui.Icon(""), Content = auto },
            new SettingsCard { Header = "Hyprism version", HeaderIcon = Ui.Icon(""), Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { status, check } } });

        Section("About");
        var about = new SettingsExpander
        {
            Header = "Hyprism " + Updater.Current.ToString(3),
            Description = "Hyprland vibes for Windows. Hyprism configures these open-source projects; each runs as its own process under its own license.",
            HeaderIcon = Ui.Icon(""),
        };
        foreach (var m in Hub.Modules)
            about.Items.Add(new SettingsCard
            {
                Header = m.Name, Description = $"github.com/{m.Repo}", Content = Ui.Text(m.License, "MutedStyle", 12),
                IsClickEnabled = true, ActionIcon = Ui.Icon("", 12),
                Command = new RelayCommand(() => Sys.OpenUrl($"https://github.com/{m.Repo}")),
            });
        Body.Children.Add(about);
    }

    void Cards(params UIElement[] cards)
    {
        var s = new StackPanel { Spacing = 4 };
        foreach (var c in cards) s.Children.Add(c);
        Body.Children.Add(s);
    }

    sealed class RelayCommand(Action run) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }
}
