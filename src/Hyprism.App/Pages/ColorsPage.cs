using Hyprism.Core;
using Hyprism.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hyprism.App.Pages;

/// <summary>The color engine: extract a palette from the wallpaper or pick a starter, preview, push everywhere.</summary>
public sealed class ColorsPage : PageBase
{
    Palette candidate = Store.Config.Palette;
    readonly Border preview = new();
    readonly StackPanel details = new() { Spacing = 12 };
    readonly Button apply = new() { Content = "Apply to every app", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };

    public ColorsPage() : base("Colors", "One palette for the terminal, Explorer tint, top bar, borders, launcher and the Windows accent.")
    {
        var fromDark = new Button { Content = "From wallpaper (dark)" };
        var fromLight = new Button { Content = "From wallpaper (light)" };
        fromDark.Click += async (_, _) => await Extract(true);
        fromLight.Click += async (_, _) => await Extract(false);
        apply.Click += async (_, _) => await Try("Apply palette", async () =>
        {
            apply.IsEnabled = false;
            var problems = new List<string>();
            await Hub.ApplyPaletteAsync(candidate, new Progress<string>(problems.Add));
            Main.Toast($"Applied \"{candidate.Name}\"", problems.Count == 0 ? "Every enabled module was recolored." : string.Join("\n", problems),
                problems.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            apply.IsEnabled = true;
        });
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, fromDark, fromLight } });

        var grid = new Grid { ColumnSpacing = 16, Margin = new Thickness(0, 16, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var right = Ui.Card(details, new Thickness(20));
        Grid.SetColumn(right, 1);
        grid.Children.Add(preview);
        grid.Children.Add(right);
        Body.Children.Add(grid);

        Section("Starter themes");
        var starters = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true };
        foreach (var p in StarterThemes.All)
        {
            var tile = new StackPanel
            {
                Width = 220, Spacing = 8, Padding = new Thickness(10),
                Children = { Ui.TerminalMock(p, 9), Ui.PaletteStrip(p, 6, 3), Ui.Text(p.Name, null, 13) },
            };
            tile.Tag = p;
            starters.Items.Add(tile);
        }
        starters.ItemClick += (_, e) => { if ((e.ClickedItem as FrameworkElement)?.Tag is Palette p) Show(p); };
        Body.Children.Add(starters);

        Show(candidate);
    }

    async Task Extract(bool dark) => await Try("Extract palette", async () =>
    {
        var wp = Store.Config.Wallpaper ?? WindowsLook.GetWallpaper() ?? throw new InvalidOperationException("Pick a wallpaper first.");
        Show(await Hub.PaletteFromWallpaperAsync(wp, dark));
    });

    void Show(Palette p)
    {
        candidate = p;
        preview.Child = Ui.TerminalMock(p, 14);
        details.Children.Clear();
        details.Children.Add(Ui.Text("PREVIEW", "EyebrowStyle"));
        details.Children.Add(Ui.Text(p.Name, null, 20));
        details.Children.Add(Ui.PaletteStrip(p, 12, 6));
        var rows = new Grid { RowSpacing = 6, ColumnSpacing = 10 };
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var named = new (string, Rgb)[] { ("Background", p.Background), ("Text", p.Foreground), ("Accent", p.Accent), ("Surface", p.Surface), ("Muted", p.Muted) };
        for (int i = 0; i < named.Length; i++)
        {
            rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var sw = Ui.Swatch(named[i].Item2, 20, 5);
            var label = Ui.Text($"{named[i].Item1}  {named[i].Item2.Hex}", "MutedStyle", 12);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(sw, i); Grid.SetRow(label, i); Grid.SetColumn(label, 1);
            rows.Children.Add(sw); rows.Children.Add(label);
        }
        details.Children.Add(rows);
        var contrast = Rgb.Contrast(p.Foreground, p.Background);
        details.Children.Add(Ui.Text($"Text contrast {contrast:0.0}:1 {(contrast >= 7 ? "(AAA)" : contrast >= 4.5 ? "(AA)" : "(low)")}", "MutedStyle", 12));
        apply.Content = p == Store.Config.Palette ? "Re-apply to every app" : $"Apply \"{p.Name}\" to every app";
    }
}
