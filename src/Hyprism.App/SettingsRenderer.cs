using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.WinUI.Controls;
using Hyprism.Core;
using Hyprism.Modules;
using Hyprism.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace Hyprism.App;

/// <summary>Builds the settings UI for any module from its <see cref="SettingDef"/> list. Edits go into <c>values</c>.</summary>
public static class SettingsRenderer
{
    public static void Render(IModule module, JsonObject values, Panel target, Action changed, Func<string, Task> invoke)
    {
        foreach (var group in module.Settings.GroupBy(d => d.Group ?? "Settings"))
        {
            target.Children.Add(Ui.Text(group.Key, "SectionStyle"));
            var stack = new StackPanel { Spacing = 4 };
            foreach (var def in group) stack.Children.Add(Card(module, def, values, changed, invoke));
            target.Children.Add(stack);
        }
    }

    static FrameworkElement Card(IModule m, SettingDef d, JsonObject values, Action changed, Func<string, Task> invoke)
    {
        var current = values[d.Key] ?? d.Default;
        void Set(JsonNode? v) { values[d.Key] = v; changed(); }

        SettingsCard Plain(object content) => new() { Header = d.Label, Description = d.Description!, Content = content };

        switch (d.Kind)
        {
            case SettingKind.Toggle:
            {
                var t = new ToggleSwitch { IsOn = current?.GetValue<bool>() ?? false, OnContent = "On", OffContent = "Off" };
                t.Toggled += (_, _) => Set(t.IsOn);
                return Plain(t);
            }
            case SettingKind.Slider:
            {
                var value = current is JsonValue jv && jv.TryGetValue<double>(out var dv) ? dv : double.Parse(current?.ToString() ?? "0", CultureInfo.InvariantCulture);
                var label = new TextBlock { Text = value.ToString(CultureInfo.InvariantCulture), MinWidth = 36, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                var s = new Slider { Minimum = d.Min, Maximum = d.Max, StepFrequency = d.Step, Value = value, Width = 220, VerticalAlignment = VerticalAlignment.Center };
                s.ValueChanged += (_, e) => { label.Text = e.NewValue.ToString(CultureInfo.InvariantCulture); Set(e.NewValue); };
                return Plain(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { label, s } });
            }
            case SettingKind.Choice:
            {
                var combo = new ComboBox { ItemsSource = d.Options, SelectedItem = current?.ToString(), MinWidth = 200 };
                var card = Plain(combo);
                if (m is IThemePreview preview && preview.PreviewKey == d.Key)
                {
                    // Live prompt preview rendered from `oh-my-posh print preview`.
                    var host = new Border
                    {
                        Background = Ui.Brush(Store.Config.Palette.Background), CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(16, 12, 16, 12), MinHeight = 64,
                    };
                    async void Refresh(string theme)
                    {
                        host.Child = new ProgressRing { IsActive = true, Width = 20, Height = 20 };
                        var ansi = await preview.PreviewAnsiAsync(theme);
                        host.Child = Ui.Ansi(ansi, Store.Config.Palette);
                    }
                    combo.SelectionChanged += (_, _) => { var v = combo.SelectedItem as string ?? ""; Set(v); Refresh(v); };
                    if (combo.SelectedItem is string first) Refresh(first);
                    var caption = Ui.Text("Live preview, rendered by Oh My Posh with your palette", "EyebrowStyle");
                    caption.Margin = new Thickness(2, 8, 0, 2);
                    return new StackPanel { Spacing = 4, Children = { card, caption, host } };
                }
                combo.SelectionChanged += (_, _) => Set(combo.SelectedItem as string);
                return card;
            }
            case SettingKind.Color:
            {
                var hex = current?.ToString() ?? "#000000";
                var swatch = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Ui.Res("ControlStrokeColorDefaultBrush") };
                var box = new TextBox { Text = hex, Width = 110, FontFamily = (FontFamily)Application.Current.Resources["MonoFont"] };
                var picker = new ColorPicker { IsAlphaEnabled = false, IsMoreButtonVisible = false };
                void Show(string h) { try { swatch.Background = Ui.Brush(Rgb.Parse(h)); } catch (FormatException) { } }
                Show(hex);
                try { var c = Rgb.Parse(hex); picker.Color = Windows.UI.Color.FromArgb(255, c.R, c.G, c.B); } catch (FormatException) { }
                picker.ColorChanged += (_, e) => { var h = new Rgb(e.NewColor.R, e.NewColor.G, e.NewColor.B).Hex; box.Text = h; };
                box.TextChanged += (_, _) => { try { Rgb.Parse(box.Text); Show(box.Text); Set(box.Text.ToUpperInvariant()); } catch (FormatException) { } };
                var button = new Button { Content = swatch, Padding = new Thickness(4), Flyout = new Flyout { Content = picker } };
                return Plain(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { button, box } });
            }
            case SettingKind.Text:
            {
                var box = new TextBox { Text = current?.ToString() ?? "", Width = 260 };
                box.TextChanged += (_, _) => Set(box.Text);
                return Plain(box);
            }
            case SettingKind.MultiLine:
            {
                var box = new TextBox
                {
                    Text = (current?.ToString() ?? "").Replace("\n", "\r"), AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                    MinHeight = 140, FontFamily = (FontFamily)Application.Current.Resources["MonoFont"], HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                box.TextChanged += (_, _) => Set(box.Text.Replace("\r", "\n"));
                return new SettingsCard { Header = d.Label, Description = d.Description!, /* null hides the line */ Content = box, ContentAlignment = ContentAlignment.Vertical, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            }
            case SettingKind.File or SettingKind.Folder:
            {
                var box = new TextBox { Text = current?.ToString() ?? "", Width = 260, PlaceholderText = "None" };
                box.TextChanged += (_, _) => Set(box.Text);
                var browse = new Button { Content = "Browse" };
                browse.Click += async (_, _) =>
                {
                    if (d.Kind == SettingKind.Folder)
                    {
                        var fp = new FolderPicker();
                        fp.FileTypeFilter.Add("*");
                        WinRT.Interop.InitializeWithWindow.Initialize(fp, App.Window.Hwnd);
                        if (await fp.PickSingleFolderAsync() is { } folder) box.Text = folder.Path;
                    }
                    else
                    {
                        var op = new FileOpenPicker();
                        op.FileTypeFilter.Add("*");
                        WinRT.Interop.InitializeWithWindow.Initialize(op, App.Window.Hwnd);
                        if (await op.PickSingleFileAsync() is { } file) box.Text = file.Path;
                    }
                };
                return Plain(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, browse } });
            }
            case SettingKind.Action:
            {
                var b = new Button { Content = "Open" };
                if (d.Key == "installFont") b.Content = "Install";
                b.Click += async (_, _) => { b.IsEnabled = false; try { await invoke(d.Key); } finally { b.IsEnabled = true; } };
                return new SettingsCard { Header = d.Label, Description = d.Description!, /* null hides the line */ Content = b, IsClickEnabled = false };
            }
            case SettingKind.KeyBindings:
                return KeyBindingEditor(d, current as JsonArray ?? [], Set);
            default:
                return Plain(new TextBlock { Text = current?.ToString() });
        }
    }

    /// <summary>Visual keybinding editor: record a shortcut, pick an action. Conflicting shortcuts are flagged.</summary>
    static FrameworkElement KeyBindingEditor(SettingDef d, JsonArray initial, Action<JsonNode?> set)
    {
        var rows = new StackPanel { Spacing = 4 };
        var list = initial.Select(b => (Keys: (string?)b?["keys"] ?? "", Action: (string?)b?["action"] ?? "")).ToList();

        void Save() => set(new JsonArray(list.Select(b => (JsonNode)new JsonObject { ["keys"] = b.Keys, ["action"] = b.Action }).ToArray()));

        void Rebuild()
        {
            rows.Children.Clear();
            var dupes = list.GroupBy(b => b.Keys).Where(g => g.Count() > 1 && g.Key.Length > 0).Select(g => g.Key).ToHashSet();
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var recorder = Ui.ShortcutRecorder(list[i].Keys, k => { list[idx] = (k, list[idx].Action); Save(); Rebuild(); });
                var action = new ComboBox { ItemsSource = Tiling.Actions, SelectedItem = list[i].Action, Width = 220 };
                action.SelectionChanged += (_, _) => { list[idx] = (list[idx].Keys, action.SelectedItem as string ?? ""); Save(); };
                var remove = new Button { Content = Ui.Icon("", 14), Padding = new Thickness(8) };
                ToolTipService.SetToolTip(remove, "Remove");
                remove.Click += (_, _) => { list.RemoveAt(idx); Save(); Rebuild(); };
                var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 6, 12, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var keyCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { recorder } };
                if (dupes.Contains(list[i].Keys)) keyCell.Children.Add(new TextBlock { Text = "Used twice", Foreground = Ui.Res("SystemFillColorCautionBrush"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
                Grid.SetColumn(action, 1); Grid.SetColumn(remove, 2);
                row.Children.Add(keyCell); row.Children.Add(action); row.Children.Add(remove);
                rows.Children.Add(new Border { Child = row, Background = Ui.Res("CardBackgroundFillColorDefaultBrush"), CornerRadius = new CornerRadius(6), BorderBrush = Ui.Res("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1) });
            }
        }
        Rebuild();

        var add = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Ui.Icon("", 12), new TextBlock { Text = "Add binding" } } } };
        add.Click += (_, _) => { list.Add(("", "focus left")); Save(); Rebuild(); };
        var reset = new HyperlinkButton { Content = "Reset to defaults" };
        reset.Click += (_, _) =>
        {
            list = ((JsonArray)d.Default!).Select(b => ((string?)b?["keys"] ?? "", (string?)b?["action"] ?? "")).ToList();
            Save(); Rebuild();
        };
        return new StackPanel
        {
            Spacing = 8,
            Children = { Ui.Text(d.Description ?? "", "MutedStyle"), rows, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, reset } } },
        };
    }
}
