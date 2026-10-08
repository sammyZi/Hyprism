using Hyprism.Core;
using Hyprism.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace Hyprism.App.Pages;

/// <summary>Overview: the current rice at a glance, quick switches, and every module's status.</summary>
public sealed class HomePage : PageBase
{
    public HomePage() : base("Overview", "Your desktop, as it is right now.")
    {
        BuildHero();
        BuildQuickToggles();
        BuildModules();
    }

    void BuildHero()
    {
        var p = Store.Config.Palette;
        var grid = new Grid { ColumnSpacing = 16, Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        // Wallpaper preview with its file name on a quiet bottom strip.
        var wp = Store.Config.Wallpaper ?? WindowsLook.GetWallpaper();
        var image = new Image { Stretch = Stretch.UniformToFill, Height = 280 };
        if (wp is not null && File.Exists(wp) && Hub.ImageExtensions[..5].Contains(System.IO.Path.GetExtension(wp).ToLowerInvariant()))
            image.Source = new BitmapImage(new Uri(wp)) { DecodePixelWidth = 900 };
        var caption = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 0, 0, 0)),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "", FontSize = 13, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
                    new TextBlock { Text = wp is null ? "No wallpaper picked yet" : System.IO.Path.GetFileName(wp), Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), FontSize = 13 },
                },
            },
        };
        var wallpaper = new Grid { CornerRadius = new CornerRadius(8), Background = Ui.Brush(p.Background), Children = { image, caption } };

        // Palette card.
        var swatches = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 8, ItemWidth = 30, ItemHeight = 30 };
        foreach (var c in p.Ansi) swatches.Children.Add(Ui.Swatch(c, 24));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        foreach (var (label, c) in new[] { ("Background", p.Background), ("Text", p.Foreground), ("Accent", p.Accent) })
            chips.Children.Add(new StackPanel { Spacing = 6, Children = { Ui.Swatch(c, 40, 10), Ui.Text(label, "EyebrowStyle"), Ui.Text(c.Hex, "MutedStyle", 12) } });

        var recolor = new Button { Content = "Recolor from wallpaper", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        recolor.Click += async (_, _) => await Try("Recolor", async () =>
        {
            var w = Store.Config.Wallpaper ?? WindowsLook.GetWallpaper() ?? throw new InvalidOperationException("Pick a wallpaper first.");
            await Hub.ApplyPaletteAsync(await Hub.PaletteFromWallpaperAsync(w));
            Main.Navigate("home");
        });
        var next = new Button { Content = "Next wallpaper" };
        next.Click += async (_, _) => await Try("Next wallpaper", async () => { await Hub.NextWallpaperAsync(); Main.Navigate("home"); });

        var paletteCard = Ui.Card(new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Ui.Text("PALETTE", "EyebrowStyle"),
                Ui.Text(p.Name, null, 20),
                chips, swatches,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { recolor, next } },
            },
        }, new Thickness(20));

        Grid.SetColumn(paletteCard, 1);
        grid.Children.Add(wallpaper);
        grid.Children.Add(paletteCard);
        Body.Children.Add(grid);
    }

    void BuildQuickToggles()
    {
        Section("Quick switches");
        var row = new Grid { ColumnSpacing = 8 };
        var items = new (string Title, string Hint, string Glyph, bool On, Func<bool, Task> Set)[]
        {
            ("Blur and glass", "Taskbar, terminal, windows", "", Hub.BlurOn, on => Hub.SetEffectsAsync(on, !Hub.PerformanceOn)),
            ("Performance mode", "Pause live wallpapers, drop blur", "", Hub.PerformanceOn, on => Hub.SetPerformanceAsync(on)),
            ("Follow wallpaper", "Recolor when it changes", "", Store.Config.FollowWallpaper, on => { Store.Config.FollowWallpaper = on; Store.Save(); return Task.CompletedTask; }),
            ("Tiling", "Start or stop the window manager", "", Store.Config.For("tiling").Enabled, _ => Hub.ToggleTilingAsync()),
        };
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var toggle = new ToggleSwitch { IsOn = it.On, OnContent = "", OffContent = "", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Right };
            toggle.Toggled += async (_, _) => await Try(it.Title, () => it.Set(toggle.IsOn));
            var top = new Grid { Children = { new FontIcon { Glyph = it.Glyph, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Left, Foreground = Ui.Res("AccentTextFillColorPrimaryBrush") }, toggle } };
            var card = Ui.Card(new StackPanel { Spacing = 4, Children = { top, Ui.Text(it.Title, null, 14), Ui.Text(it.Hint, "MutedStyle", 12) } }, new Thickness(16, 12, 12, 14));
            Grid.SetColumn(card, i);
            row.Children.Add(card);
        }
        Body.Children.Add(row);
    }

    void BuildModules()
    {
        Section("Modules");
        var list = new StackPanel { Spacing = 2 };
        foreach (var m in Hub.Modules)
        {
            var state = new Ui.StatusPill();
            state.Set(Ui.Status.Waiting, "Checking…");
            var g = new Grid { ColumnSpacing = 14, Padding = new Thickness(14, 10, 14, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel { Children = { Ui.Text(m.Name, null, 14), Ui.Text(m.Repo, "MutedStyle", 12) } };
            var icon = new FontIcon { Glyph = m.Glyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1); Grid.SetColumn(state, 2);
            g.Children.Add(icon); g.Children.Add(name); g.Children.Add(state);

            var button = new Button
            {
                Content = g, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(0), Background = Ui.Res("CardBackgroundFillColorDefaultBrush"), BorderBrush = Ui.Res("CardStrokeColorDefaultBrush"),
                CornerRadius = new CornerRadius(6),
            };
            button.Click += (_, _) => Main.Navigate("module:" + m.Id);
            list.Children.Add(button);
            _ = Fill(m, state);
        }
        Body.Children.Add(list);
    }

    static async Task Fill(IModule m, Ui.StatusPill state)
    {
        ModuleStatus s;
        try { s = await Task.Run(() => m.GetStatusAsync()); } catch { s = ModuleStatus.Missing; }
        bool healthy = m.RunsInBackground ? s.Running : Store.Config.For(m.Id).Enabled;
        if (!s.Installed) state.Set(Ui.Status.Waiting, "Not installed");
        else if (s.UpdateAvailable) state.Set(Ui.Status.Warning, "Update available");
        else if (healthy) state.Set(Ui.Status.Done, m.RunsInBackground ? "Running" : "Themed");
        else state.Set(Ui.Status.Waiting, "Installed");
    }
}
