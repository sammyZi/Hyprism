using Hyprism.Core;
using Hyprism.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace Hyprism.App.Pages;

/// <summary>Profiles ("rices"): save the whole setup, switch instantly, share as .hyprism files.</summary>
public sealed class ProfilesPage : PageBase
{
    readonly StackPanel list = new() { Spacing = 8 };

    public ProfilesPage() : base("Profiles", "A profile is the whole setup: wallpaper, colors, blur, bar, borders and keybindings.")
    {
        var name = new TextBox { PlaceholderText = "Name, e.g. Tokyo night coding", Width = 280 };
        var save = new Button { Content = "Save current setup", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { name.Focus(FocusState.Programmatic); return; }
            Hub.SaveProfile(name.Text.Trim());
            name.Text = "";
            Main.Toast("Profile saved", null, InfoBarSeverity.Success);
            Load();
        };
        var import = new Button { Content = "Import .hyprism" };
        import.Click += async (_, _) =>
        {
            var op = new FileOpenPicker();
            op.FileTypeFilter.Add(".hyprism");
            WinRT.Interop.InitializeWithWindow.Initialize(op, Main.Hwnd);
            if (await op.PickSingleFileAsync() is not { } f) return;
            await Try("Import", () => { var p = Hub.ImportProfile(f.Path); Main.Toast($"Imported \"{p.Name}\"", null, InfoBarSeverity.Success); Load(); return Task.CompletedTask; });
        };
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { name, save, import } });

        Section("Saved");
        Body.Children.Add(list);
        Load();

        Section("Starter themes");
        Body.Children.Add(Ui.Text("Applies only the colors. Save afterwards to keep it as a profile.", "MutedStyle"));
        var starters = new GridView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var p in StarterThemes.All)
            starters.Items.Add(new StackPanel { Tag = p, Width = 200, Spacing = 8, Padding = new Thickness(8), Children = { Ui.PaletteStrip(p, 28, 6), Ui.Text(p.Name, null, 13) } });
        starters.ItemClick += async (_, e) =>
        {
            if ((e.ClickedItem as FrameworkElement)?.Tag is Palette p) await Try("Apply theme", () => Hub.ApplyPaletteAsync(p));
        };
        Body.Children.Add(starters);
    }

    void Load()
    {
        list.Children.Clear();
        var profiles = Hub.Profiles();
        if (profiles.Count == 0) { list.Children.Add(Ui.Card(Ui.Text("No profiles yet. Set things up the way you like, then save them above.", "MutedStyle"))); return; }
        foreach (var p in profiles) list.Children.Add(Row(p));
    }

    FrameworkElement Row(Profile p)
    {
        var thumb = new Grid { Width = 120, Height = 68, CornerRadius = new CornerRadius(6), Background = Ui.Brush(p.Palette.Background) };
        if (p.Wallpaper is { } wp && File.Exists(wp) && Hub.ImageExtensions[..5].Contains(Path.GetExtension(wp).ToLowerInvariant()))
            thumb.Children.Add(new Image { Source = new BitmapImage(new Uri(wp)) { DecodePixelWidth = 240 }, Stretch = Stretch.UniformToFill });

        var tags = new List<string>();
        if (Store.Config.ActiveProfile == p.Name) tags.Add("Active");
        if (Store.Config.LightProfile == p.Name) tags.Add("Light mode");
        if (Store.Config.DarkProfile == p.Name) tags.Add("Dark mode");
        var info = new StackPanel
        {
            Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Ui.Text(p.Name, null, 15),
                Ui.Text($"{p.Palette.Name} · {p.Modules.Count(m => m.Value.Enabled)} modules · saved {p.Saved:d MMM yyyy}{(tags.Count > 0 ? " · " + string.Join(", ", tags) : "")}", "MutedStyle", 12),
                new Border { Width = 180, HorizontalAlignment = HorizontalAlignment.Left, Child = Ui.PaletteStrip(p.Palette, 6, 3) },
            },
        };

        var apply = new Button { Content = "Apply", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        apply.Click += async (_, _) => await Try("Apply profile", async () =>
        {
            apply.IsEnabled = false;
            var problems = new List<string>();
            await Hub.ApplyProfileAsync(p, new Progress<string>(problems.Add));
            if (problems.Count > 0) Main.Toast("Applied with warnings", string.Join("\n", problems), InfoBarSeverity.Warning);
            Load();
        });

        var more = new DropDownButton { Content = "More" };
        var menu = new MenuFlyout();
        void Add(string text, Func<Task> act) { var i = new MenuFlyoutItem { Text = text }; i.Click += async (_, _) => await Try(text, act); menu.Items.Add(i); }
        Add("Export…", async () =>
        {
            var sp = new FileSavePicker { SuggestedFileName = p.Name };
            sp.FileTypeChoices.Add("Hyprism profile", [".hyprism"]);
            WinRT.Interop.InitializeWithWindow.Initialize(sp, Main.Hwnd);
            if (await sp.PickSaveFileAsync() is not { } f) return;
            if (File.Exists(f.Path)) File.Delete(f.Path); // the picker may create an empty file
            Hub.ExportProfile(p, f.Path);
            Main.Toast("Exported", f.Path, InfoBarSeverity.Success);
        });
        Add("Update with current setup", () => { Hub.SaveProfile(p.Name); Load(); return Task.CompletedTask; });
        Add("Use in light mode", () => { Store.Config.LightProfile = p.Name; Store.Save(); Load(); return Task.CompletedTask; });
        Add("Use in dark mode", () => { Store.Config.DarkProfile = p.Name; Store.Save(); Load(); return Task.CompletedTask; });
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Delete", async () =>
        {
            if (!await Confirm($"Delete \"{p.Name}\"?", "The profile file is removed. Your current setup isn't changed.", "Delete")) return;
            Hub.DeleteProfile(p.Name);
            Load();
        });
        more.Flyout = menu;

        var g = new Grid { ColumnSpacing = 16 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { apply, more } };
        Grid.SetColumn(info, 1); Grid.SetColumn(actions, 2);
        g.Children.Add(thumb); g.Children.Add(info); g.Children.Add(actions);
        return Ui.Card(g, new Thickness(12));
    }
}
