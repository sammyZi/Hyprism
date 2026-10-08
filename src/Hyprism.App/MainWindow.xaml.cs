using H.NotifyIcon;
using Hyprism.App.Pages;
using Hyprism.Core;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Hyprism.App;

public sealed partial class MainWindow : Window
{
    public IntPtr Hwnd { get; }
    readonly Hotkeys hotkeys;
    TaskbarIcon? tray;
    bool exiting;
    readonly DispatcherQueueTimer perfTimer, rotateTimer;

    public MainWindow()
    {
        InitializeComponent();
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // No system backdrop: Root paints Hyprism's own AppBackgroundBrush (see Styles.xaml).
        ExtendsContentIntoTitleBar = true;
        Root.ActualThemeChanged += (_, _) => StyleCaptionButtons();
        if (!Perf.NoMotion) Motion.Attach(Root);
        Perf.Start();
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Hyprism.ico"));
        // AppWindow sizes are physical pixels: scale by DPI, then keep it inside the work area.
        var dpi = GetDpiForWindow(Hwnd) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = Math.Min((int)(1280 * dpi), (int)(work.Width * 0.92)), h = Math.Min((int)(860 * dpi), (int)(work.Height * 0.92));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
        ApplyAppTheme();

        AppWindow.Closing += (_, e) =>
        {
            if (exiting) return;
            e.Cancel = true; // close = hide to tray; Exit lives in the tray menu
            AppWindow.Hide();
        };

        BuildNav();
        CreateTray();
        hotkeys = new Hotkeys(Hwnd);
        RegisterHotkeys();

        Hub.Notify += msg => DispatcherQueue.TryEnqueue(() => Toast(msg, null));
        Hub.Changed += () => DispatcherQueue.TryEnqueue(UpdateChips);
        Sys.ConfirmUnverified = ConfirmUnverifiedAsync;
        UpdateChips();

        perfTimer = DispatcherQueue.CreateTimer();
        perfTimer.Interval = TimeSpan.FromSeconds(5);
        perfTimer.Tick += async (_, _) => { try { await Hub.CheckAutoPerformanceAsync(); } catch { } };
        perfTimer.Start();

        rotateTimer = DispatcherQueue.CreateTimer();
        rotateTimer.Tick += async (_, _) => { try { await Hub.NextWallpaperAsync(); } catch (Exception ex) { Toast("Wallpaper rotation", ex.Message); } };
        UpdateRotation();

        Navigate("home");
        // First launch: snapshot the stock Windows look before anything can change it, so "Restore original look"
        // and the uninstaller always have something to go back to. Runs once, in the background.
        _ = Task.Run(async () =>
        {
            try { WindowsLook.RepairMissingWallpaper(); } catch { /* best effort */ }
            if (!WindowsLook.HasOriginalBackup) await WindowsLook.BackupOriginalAsync();
        });
        _ = CheckForUpdatesAsync();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Once a day, if an update source is set: tell the user a new version exists (installing is their call).</summary>
    async Task CheckForUpdatesAsync()
    {
        var c = Store.Config;
        if (!c.AutoCheckUpdates || string.IsNullOrWhiteSpace(c.UpdateRepo) || DateTime.UtcNow - c.LastUpdateCheck < TimeSpan.FromDays(1)) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20)); // never compete with startup
            var update = await Updater.CheckAsync();
            c.LastUpdateCheck = DateTime.UtcNow;
            Store.Save();
            if (update is not null) Toast($"Hyprism {update.Tag} is available", "Open Settings > Updates to install it.", InfoBarSeverity.Success);
        }
        catch { /* offline: try again next start */ }
    }

    public void ApplyAppTheme()
    {
        Root.RequestedTheme = Store.Config.AppTheme switch
        {
            "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default,
        };
        StyleCaptionButtons();
    }

    /// <summary>The min/max/close buttons don't follow the app theme on their own; match them to our background.</summary>
    void StyleCaptionButtons()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var tb = AppWindow.TitleBar;
        var fg = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        tb.ButtonBackgroundColor = tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        tb.ButtonForegroundColor = tb.ButtonHoverForegroundColor = tb.ButtonPressedForegroundColor = fg;
        tb.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0x80, fg.R, fg.G, fg.B);
        tb.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x18, fg.R, fg.G, fg.B);
        tb.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x10, fg.R, fg.G, fg.B);
    }

    public void UpdateRotation()
    {
        rotateTimer.Stop();
        if (Store.Config.RotateMinutes > 0)
        {
            rotateTimer.Interval = TimeSpan.FromMinutes(Store.Config.RotateMinutes);
            rotateTimer.Start();
        }
    }

    void UpdateChips()
    {
        ProfileChip.Visibility = Store.Config.ActiveProfile is null ? Visibility.Collapsed : Visibility.Visible;
        ProfileLabel.Text = Store.Config.ActiveProfile ?? "";
        PerfChip.Visibility = Hub.PerformanceOn ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- navigation ----------

    void BuildNav()
    {
        Nav.MenuItems.Clear();
        Nav.MenuItems.Add(Item("Overview", "", "home"));
        Nav.MenuItems.Add(Item("Colors", "", "colors"));
        Nav.MenuItems.Add(Item("Wallpapers", "", "wallpapers"));
        Nav.MenuItems.Add(Item("Profiles", "", "profiles"));
        foreach (var group in Hub.Modules.GroupBy(m => m.Category))
        {
            Nav.MenuItems.Add(new NavigationViewItemHeader { Content = group.Key.ToString() });
            foreach (var m in group) Nav.MenuItems.Add(Item(m.Name, m.Glyph, "module:" + m.Id));
        }
    }

    static NavigationViewItem Item(string text, string glyph, string tag) =>
        new() { Content = text, Icon = new FontIcon { Glyph = glyph }, Tag = tag };

    void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected) { ContentFrame.Content = new SettingsPage(); return; }
        if (args.SelectedItem is NavigationViewItem { Tag: string tag }) Open(tag);
    }

    void Open(string tag)
    {
        switch (tag)
        {
            // Pages are code-only, so they're created directly (Frame.Navigate needs XAML type metadata).
            case "home": ContentFrame.Content = new HomePage(); break;
            case "colors": ContentFrame.Content = new ColorsPage(); break;
            case "wallpapers": ContentFrame.Content = new WallpapersPage(); break;
            case "profiles": ContentFrame.Content = new ProfilesPage(); break;
            case var t when t.StartsWith("module:"): ContentFrame.Content = new ModulePage(t[7..]); break;
        }
    }

    /// <summary>Selects the matching nav item (which navigates).</summary>
    public void Navigate(string tag)
    {
        var item = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
        if (item is null) { Open(tag); return; }
        if (ReferenceEquals(Nav.SelectedItem, item)) Open(tag); else Nav.SelectedItem = item;
    }

    // ---------- tray ----------

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#135")] static extern int SetPreferredAppMode(int mode);
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#136")] static extern void FlushMenuThemes();

    /// <summary>
    /// Native menus are light unless the process opts in. uxtheme's SetPreferredAppMode(AllowDark) is the call
    /// Explorer, Notepad++ and Windows Terminal use; menus then follow the Windows light/dark setting.
    /// </summary>
    static void UseDarkNativeMenus()
    {
        try { SetPreferredAppMode(1 /* AllowDark */); FlushMenuThemes(); }
        catch { /* older Windows: menus just stay light */ }
    }

    void CreateTray()
    {
        UseDarkNativeMenus();
        var menu = new MenuFlyout();
        void Add(string text, Func<Task> action)
        {
            // The native popup menu (below) invokes Command, not Click.
            var cmd = new XamlUICommand();
            cmd.ExecuteRequested += (_, _) => DispatcherQueue.TryEnqueue(async () =>
            {
                try { await action(); } catch (Exception ex) { Toast(text, ex.Message); }
            });
            menu.Items.Add(new MenuFlyoutItem { Text = text, Command = cmd });
        }
        Add("Open Hyprism", () => { ShowFromTray(); return Task.CompletedTask; });
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Next wallpaper", Hub.NextWallpaperAsync);
        Add("Next profile", Hub.NextProfileAsync);
        Add("Toggle blur", Hub.ToggleBlurAsync);
        Add("Performance mode", () => Hub.SetPerformanceAsync(!Hub.PerformanceOn));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Exit", () => { ExitApp(); return Task.CompletedTask; });

        var open = new XamlUICommand();
        open.ExecuteRequested += (_, _) => ShowFromTray();
        tray = new TaskbarIcon
        {
            ToolTipText = "Hyprism",
            IconSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Hyprism.ico"))),
            // Native Win32 menu: sizes itself to fit (the XAML "second window" mode clipped the items and showed a
            // scrollbar) and appears instantly. Dark mode comes from UseDarkNativeMenus().
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            LeftClickCommand = open,
            NoLeftClickDelay = true,
        };
        tray.ForceCreate();
    }

    public void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
        Maximize();
    }

    /// <summary>Hyprism always opens maximized (the work area, taskbar stays visible).</summary>
    public void Maximize()
    {
        if (AppWindow.Presenter is OverlappedPresenter p && p.State != OverlappedPresenterState.Maximized) p.Maximize();
    }

    void ExitApp()
    {
        exiting = true;
        hotkeys.Dispose();
        tray?.Dispose();
        Close();
        Application.Current.Exit();
    }

    // ---------- hotkeys ----------

    public void RegisterHotkeys()
    {
        hotkeys.Clear();
        var map = new Dictionary<string, Func<Task>>
        {
            ["switchProfile"] = Hub.NextProfileAsync,
            ["nextWallpaper"] = Hub.NextWallpaperAsync,
            ["toggleTiling"] = Hub.ToggleTilingAsync,
            ["toggleBlur"] = Hub.ToggleBlurAsync,
            ["performance"] = () => Hub.SetPerformanceAsync(!Hub.PerformanceOn),
        };
        var failed = new List<string>();
        foreach (var (name, keys) in Store.Config.Hotkeys)
        {
            if (string.IsNullOrWhiteSpace(keys) || !map.TryGetValue(name, out var act)) continue;
            if (!hotkeys.Register(keys, () => DispatcherQueue.TryEnqueue(async () =>
                { try { await act(); } catch (Exception ex) { Toast("Hotkey", ex.Message); } })))
                failed.Add(Ui.Pretty(keys));
        }
        if (failed.Count > 0) Toast("Some hotkeys are taken", $"{string.Join(", ", failed)} already belong to another app.");
    }

    // ---------- notifications ----------

    public void Toast(string title, string? message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        if (!AppWindow.IsVisible && tray is not null)
        {
            tray.ShowNotification(title, message ?? "");
            return;
        }
        var bar = new InfoBar
        {
            Title = title, Message = message ?? "", Severity = severity, IsOpen = true,
            Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorTertiaryBrush"],
        };
        bar.Closed += (_, _) => Toasts.Children.Remove(bar);
        Toasts.Children.Add(bar);
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(7);
        t.IsRepeating = false;
        t.Tick += (_, _) => Toasts.Children.Remove(bar);
        t.Start();
    }

    async Task<bool> ConfirmUnverifiedAsync(string asset, string sha256)
    {
        var tcs = new TaskCompletionSource<bool>();
        DispatcherQueue.TryEnqueue(async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                Title = "No published checksum",
                Content = Ui.Text($"The project didn't publish a SHA256 for {asset}, so Hyprism can't verify it against the release.\n\n" +
                                  $"Downloaded file SHA256:\n{sha256}\n\nInstall it anyway?", "MutedStyle"),
                PrimaryButtonText = "Install anyway",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            tcs.SetResult(await dialog.ShowAsync() == ContentDialogResult.Primary);
        });
        return await tcs.Task;
    }
}
