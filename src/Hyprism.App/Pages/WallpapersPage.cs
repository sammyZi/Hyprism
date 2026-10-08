using System.Collections.ObjectModel;
using CommunityToolkit.WinUI.Controls;
using Hyprism.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.System;

namespace Hyprism.App.Pages;

/// <summary>Wallpapers: online search (Wallhaven, Openverse, NASA, Unsplash via Picsum), your folder, rotation and transitions.</summary>
public sealed class WallpapersPage : PageBase
{
    readonly ItemsRepeater gallery = new();
    readonly TextBlock empty = Ui.Text("", "MutedStyle");

    public WallpapersPage() : base("Wallpapers", "Search free wallpapers or pick from your folder. With Live Wallpapers on, every change animates like swww.")
    {
        var c = Store.Config;
        var folder = new TextBox { Text = c.WallpaperFolder ?? "", PlaceholderText = "No folder", Width = 300, IsReadOnly = true };
        var browse = new Button { Content = "Choose folder" };
        browse.Click += async (_, _) =>
        {
            var fp = new FolderPicker();
            fp.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(fp, Main.Hwnd);
            if (await fp.PickSingleFolderAsync() is not { } f) return;
            c.WallpaperFolder = folder.Text = f.Path;
            Store.Save();
            LoadGallery();
        };
        var transition = new ComboBox { ItemsSource = new[] { "grow", "fade", "wipe", "slide", "outer", "random" }, SelectedItem = c.Transition, MinWidth = 160 };
        transition.SelectionChanged += (_, _) => { c.Transition = (string)transition.SelectedItem; Store.Save(); };
        var rotate = new NumberBox { Value = c.RotateMinutes, Minimum = 0, Maximum = 1440, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 120 };
        rotate.ValueChanged += (_, e) => { c.RotateMinutes = double.IsNaN(e.NewValue) ? 0 : (int)e.NewValue; Store.Save(); Main.UpdateRotation(); };
        var follow = new ToggleSwitch { IsOn = c.FollowWallpaper };
        follow.Toggled += (_, _) => { c.FollowWallpaper = follow.IsOn; Store.Save(); };

        var transitionsHint = Hub.Modules.OfType<IWallpaperHost>().Any(h => h.IsActive)
            ? "Applied through Lively." : "Turn on Live Wallpapers to animate changes; otherwise Windows switches instantly.";

        BuildDiscover();

        Section("Your folder");
        Body.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new SettingsCard { Header = "Wallpaper folder", Description = "Downloads go to their own folder automatically if you haven't chosen one.", Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { folder, browse } } },
                new SettingsCard { Header = "Transition", Description = transitionsHint, Content = transition },
                new SettingsCard { Header = "Rotate every (minutes)", Description = "0 turns rotation off. The Next wallpaper hotkey works either way.", Content = rotate },
                new SettingsCard { Header = "Recolor everything from the wallpaper", Description = "Runs the color engine on each change.", Content = follow },
            },
        });
        // ItemsRepeater virtualizes inside the page's ScrollViewer: only tiles on screen exist, so scrolling
        // a folder of hundreds of wallpapers stays at full frame rate.
        gallery.Layout = Grid4();
        gallery.ItemTemplate = new TileFactory((path, target) => Try("Set wallpaper", () => Hub.SetWallpaperAsync(path, target)));
        gallery.Margin = new Thickness(0, 8, 0, 0);
        Body.Children.Add(empty);
        Body.Children.Add(gallery);
        LoadGallery();
    }

    static UniformGridLayout Grid4() => new() { MinItemWidth = 232, MinItemHeight = 132, MinRowSpacing = 8, MinColumnSpacing = 8, ItemsStretch = UniformGridLayoutItemsStretch.Fill };

    void LoadGallery()
    {
        var files = Hub.WallpapersInFolder();
        empty.Text = Store.Config.WallpaperFolder is null ? "Choose a folder to see its wallpapers here." : "No images or videos in that folder.";
        empty.Visibility = files.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        gallery.ItemsSource = files;
    }

    // ---------- Discover: Wallhaven, Openverse, NASA search + random Unsplash (Picsum) ----------

    readonly ObservableCollection<OnlinePhoto> online = [];
    readonly Ui.StatusPill onlineStatus = new();
    readonly AutoSuggestBox search = new() { PlaceholderText = "Search wallpapers, e.g. mountains, neon city, nebula", QueryIcon = new SymbolIcon(Symbol.Find), Width = 380 };
    readonly ComboBox sourceBox = new() { MinWidth = 250 };
    readonly HyperlinkButton credit = new() { Padding = new Thickness(0) };
    readonly Button more = new() { Content = "Load more" };
    PhotoSource source = PhotoSource.Wallhaven;
    string query = "";
    int page = 1;
    CancellationTokenSource? loading;

    void BuildDiscover()
    {
        Section("Discover");
        foreach (var s in Enum.GetValues<PhotoSource>()) sourceBox.Items.Add(new ComboBoxItem { Content = OnlineWallpapers.Describe(s), Tag = s });
        sourceBox.SelectedIndex = 0;
        sourceBox.SelectionChanged += async (_, _) =>
        {
            source = (PhotoSource)((ComboBoxItem)sourceBox.SelectedItem).Tag;
            search.IsEnabled = OnlineWallpapers.CanSearch(source);
            if (!search.IsEnabled) { search.Text = ""; query = ""; }
            search.PlaceholderText = source switch
            {
                PhotoSource.Nasa => "Search NASA, e.g. nebula, galaxy, earth at night",
                PhotoSource.Openverse => "Search Creative Commons photos, e.g. forest, ocean",
                PhotoSource.Picsum => "Random photos only (Lorem Picsum has no search)",
                _ => "Search wallpapers, e.g. mountains, neon city, anime",
            };
            UpdateCredit();
            page = source == PhotoSource.Picsum ? Random.Shared.Next(1, OnlineWallpapers.PicsumPageCount + 1) : 1;
            await LoadOnlineAsync(append: false);
        };
        search.QuerySubmitted += async (_, e) => { query = e.QueryText; page = 1; await LoadOnlineAsync(append: false); };
        search.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) search.Text = ""; };

        var shuffle = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Icon("", 14), new TextBlock { Text = "Shuffle" } } } };
        ToolTipService.SetToolTip(shuffle, "Random page of results");
        shuffle.Click += async (_, _) =>
        {
            page = Random.Shared.Next(1, source == PhotoSource.Picsum ? OnlineWallpapers.PicsumPageCount + 1 : 11);
            await LoadOnlineAsync(append: false);
        };
        more.Click += async (_, _) => { page++; await LoadOnlineAsync(append: true); };

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { sourceBox, search } };
        var row2 = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { shuffle, more, onlineStatus } };
        onlineStatus.HorizontalAlignment = HorizontalAlignment.Left;
        credit.HorizontalAlignment = HorizontalAlignment.Right;
        credit.VerticalAlignment = VerticalAlignment.Center;
        row2.Children.Add(left); row2.Children.Add(credit);
        Body.Children.Add(row1);
        Body.Children.Add(row2);
        Body.Children.Add(Ui.Text("Click a photo to set it as your desktop wallpaper, lock screen, or both. Desktop wallpapers also recolor everything to match.", "MutedStyle"));
        Body.Children.Add(new ItemsRepeater { Layout = Grid4(), ItemTemplate = new OnlineTileFactory(PickOnlineAsync), ItemsSource = online, Margin = new Thickness(0, 8, 0, 0) });

        UpdateCredit();
        _ = LoadOnlineAsync(append: false);
    }

    void UpdateCredit()
    {
        credit.Content = source switch
        {
            PhotoSource.Wallhaven => "Wallpapers from wallhaven.cc",
            PhotoSource.Openverse => "Creative Commons images via Openverse",
            PhotoSource.Nasa => "Public-domain imagery from NASA",
            _ => "Free Unsplash photos via Lorem Picsum",
        };
        credit.NavigateUri = new Uri(OnlineWallpapers.Homepage(source));
    }

    (int W, int H) ScreenSize()
    {
        // Physical pixels of the screen Hyprism is on: search filters and downloads match it.
        var b = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(Main.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).OuterBounds;
        return (b.Width, b.Height);
    }

    async Task LoadOnlineAsync(bool append)
    {
        loading?.Cancel();
        var cts = loading = new CancellationTokenSource();
        onlineStatus.Set(Ui.Status.Working, "Loading…");
        more.IsEnabled = false;
        try
        {
            var (w, h) = ScreenSize();
            var photos = await OnlineWallpapers.SearchAsync(source, query, page, w, h, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (!append) online.Clear();
            foreach (var p in photos) online.Add(p);
            more.IsEnabled = photos.Count > 0;
            onlineStatus.Set(online.Count > 0 ? Ui.Status.Done : Ui.Status.Warning,
                online.Count > 0 ? $"{online.Count} photos" : string.IsNullOrWhiteSpace(query) ? "Nothing here" : $"No results for \"{query}\"");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            onlineStatus.Set(Ui.Status.Failed, ex is HttpRequestException
                ? $"Couldn't reach {OnlineWallpapers.Describe(source).Split(' ')[0]} (offline or rate-limited)" : ex.Message);
        }
    }

    /// <param name="target">Where to apply it; null = download only.</param>
    async Task PickOnlineAsync(OnlinePhoto photo, WallpaperTarget? target, Action<bool> busy)
    {
        busy(true);
        onlineStatus.Set(Ui.Status.Working, "Downloading…");
        try
        {
            var (w, h) = ScreenSize();
            var file = await OnlineWallpapers.DownloadAsync(photo, w, h);
            // First download with no folder chosen: use the download folder, so rotation and "Next wallpaper" work.
            if (Store.Config.WallpaperFolder is null) { Store.Config.WallpaperFolder = Path.GetDirectoryName(file); Store.Save(); }
            if (target is { } t)
            {
                onlineStatus.Set(Ui.Status.Working, "Applying…");
                await Hub.SetWallpaperAsync(file, t);
            }
            onlineStatus.Set(Ui.Status.Done, target switch
            {
                WallpaperTarget.Desktop => "Set as desktop wallpaper",
                WallpaperTarget.LockScreen => "Set as lock screen",
                WallpaperTarget.Both => "Set on desktop and lock screen",
                _ => "Saved to your wallpaper folder",
            });
            LoadGallery();
        }
        catch (Exception ex) { onlineStatus.Set(Ui.Status.Failed, (target is null ? "Download failed: " : "Couldn't apply: ") + ex.Message); }
        finally { busy(false); }
    }

    /// <summary>The "Set as" menu shared by online and folder tiles. Opens on left or right click.</summary>
    static MenuFlyout SetAsMenu(Func<WallpaperTarget?, Task> apply, bool downloadOption, Action? openSource)
    {
        var menu = new MenuFlyout();
        void Add(string text, string glyph, WallpaperTarget? target)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += async (_, _) => await apply(target);
            menu.Items.Add(item);
        }
        Add("Set as desktop wallpaper", "", WallpaperTarget.Desktop);
        Add("Set as lock screen", "", WallpaperTarget.LockScreen);
        Add("Set as both", "", WallpaperTarget.Both);
        if (downloadOption || openSource is not null) menu.Items.Add(new MenuFlyoutSeparator());
        if (downloadOption) Add("Download only", "", null);
        if (openSource is not null)
        {
            var src = new MenuFlyoutItem { Text = "Open source page", Icon = new FontIcon { Glyph = "" } };
            src.Click += (_, _) => openSource();
            menu.Items.Add(src);
        }
        return menu;
    }

    /// <summary>Online photo tiles: thumbnail, credit, a spinner while downloading. Recycled as you scroll.</summary>
    sealed class OnlineTileFactory(Func<OnlinePhoto, WallpaperTarget?, Action<bool>, Task> pick) : IElementFactory
    {
        readonly Stack<Button> pool = new();

        public UIElement GetElement(ElementFactoryGetArgs args)
        {
            var photo = (OnlinePhoto)args.Data;
            var b = pool.Count > 0 ? pool.Pop() : Create();
            b.Tag = photo;
            var grid = (Grid)b.Content;
            var image = (Image)grid.Children[0];
            image.Source = null;
            image.Tag = photo;
            _ = LoadThumbAsync(image, photo);
            ((TextBlock)((Border)grid.Children[1]).Child).Text = photo.Credit;
            grid.Children[2].Visibility = Visibility.Collapsed;
            ToolTipService.SetToolTip(b, photo.Attribution);
            return b;
        }

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
            if (args.Element is not Button b) return;
            var image = (Image)((Grid)b.Content).Children[0];
            image.Source = null;
            image.Tag = null;
            pool.Push(b);
        }

        static async Task LoadThumbAsync(Image image, OnlinePhoto photo)
        {
            try
            {
                var bytes = await OnlineWallpapers.ThumbAsync(photo);
                if (!ReferenceEquals(image.Tag, photo)) return; // tile was recycled for another photo meanwhile
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes));
                stream.Seek(0);
                var bmp = new BitmapImage { DecodePixelWidth = 464, DecodePixelType = DecodePixelType.Physical };
                await bmp.SetSourceAsync(stream);
                if (ReferenceEquals(image.Tag, photo)) image.Source = bmp;
            }
            catch { /* offline or blocked: the tile keeps its placeholder */ }
        }

        Button Create()
        {
            var label = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0)),
                Child = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), TextTrimming = TextTrimming.CharacterEllipsis },
            };
            var spinner = new Grid
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 0, 0, 0)), Visibility = Visibility.Collapsed,
                Children = { new ProgressRing { IsActive = true, Width = 28, Height = 28 } },
            };
            var grid = new Grid
            {
                Height = 132, Background = Ui.Res("SubtleFillColorSecondaryBrush"),
                Children = { new Image { Stretch = Stretch.UniformToFill }, label, spinner },
            };
            var b = new Button
            {
                Content = grid, Padding = new Thickness(0), CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            void Busy(bool on) => spinner.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            var menu = SetAsMenu(
                async target => { if (b.Tag is OnlinePhoto p && spinner.Visibility != Visibility.Visible) await pick(p, target, Busy); },
                downloadOption: true,
                openSource: () => { if (b.Tag is OnlinePhoto p && p.PageUrl.StartsWith("https://")) Sys.OpenUrl(p.PageUrl); });
            b.Flyout = menu;        // left click
            b.ContextFlyout = menu; // right click
            return b;
        }
    }

    /// <summary>Builds wallpaper tiles on demand and reuses the ones that scroll out of view.</summary>
    sealed class TileFactory(Func<string, WallpaperTarget, Task> pick) : IElementFactory
    {
        readonly Stack<Button> pool = new();

        public UIElement GetElement(ElementFactoryGetArgs args)
        {
            var path = (string)args.Data;
            var b = pool.Count > 0 ? pool.Pop() : Create();
            b.Tag = path;
            var grid = (Grid)b.Content;
            var image = (Image)grid.Children[0];
            bool isVideo = Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".webm" or ".mkv";
            // Decoded off the UI thread at thumbnail size, never the full-resolution image.
            image.Source = isVideo ? null : new BitmapImage(new Uri(path)) { DecodePixelWidth = 320, DecodePixelType = DecodePixelType.Logical };
            grid.Children[1].Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)((Border)grid.Children[2]).Child).Text = Path.GetFileName(path);
            bool current = path == Store.Config.Wallpaper;
            b.BorderBrush = current ? Ui.Res("AccentFillColorDefaultBrush") : null;
            b.BorderThickness = new Thickness(current ? 2 : 0);
            return b;
        }

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
            if (args.Element is not Button b) return;
            ((Image)((Grid)b.Content).Children[0]).Source = null; // release the decoded thumbnail
            pool.Push(b);
        }

        Button Create()
        {
            var label = new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(8, 4, 8, 4),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0)),
                Child = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), TextTrimming = TextTrimming.CharacterEllipsis },
            };
            var grid = new Grid
            {
                Height = 132, Background = Ui.Res("SubtleFillColorSecondaryBrush"),
                Children = { new Image { Stretch = Stretch.UniformToFill }, new FontIcon { Glyph = "", FontSize = 28 }, label },
            };
            var b = new Button
            {
                Content = grid, Padding = new Thickness(0), CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            var menu = SetAsMenu(async target => { if (b.Tag is string p) await pick(p, target ?? WallpaperTarget.Desktop); }, downloadOption: false, openSource: null);
            b.Flyout = menu;
            b.ContextFlyout = menu;
            return b;
        }
    }
}
