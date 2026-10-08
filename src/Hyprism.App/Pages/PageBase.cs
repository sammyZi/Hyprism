using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Hyprism.App.Pages;

/// <summary>Scrolling, width-capped page with a title. Pages add their content to <see cref="Body"/>.</summary>
public abstract class PageBase : Page
{
    protected readonly StackPanel Body = new() { Spacing = 8 };
    protected readonly StackPanel Header = new() { Spacing = 6, Margin = new Thickness(0, 0, 0, 12) };
    protected MainWindow Main => App.Window;

    protected PageBase(string title, string? subtitle = null)
    {
        // Sections cascade in (compositor-driven); the header slides in first.
        if (!Perf.NoTransitions)
        {
            Header.Transitions = [new EntranceThemeTransition { FromVerticalOffset = 16 }];
            Body.ChildrenTransitions = [new EntranceThemeTransition { FromVerticalOffset = 36, IsStaggeringEnabled = true }];
        }
        Header.Children.Add(Ui.Text(title, "PageTitleStyle"));
        if (subtitle is not null) Header.Children.Add(Ui.Text(subtitle, "MutedStyle", 14));
        var column = new StackPanel
        {
            MaxWidth = (double)Application.Current.Resources["ContentMaxWidth"],
            Padding = (Thickness)Application.Current.Resources["PagePadding"],
            Children = { Header, Body },
        };
        // Classic ScrollViewer, measured on a 144Hz laptop (Hyprism --perf, real wheel input): it holds 141-144fps and
        // moves 3x further per wheel notch than the newer ScrollView, whose short wheel steps felt like lag
        // (microsoft-ui-xaml#10404). `--scrollview` switches back for comparison.
        Content = !Perf.UseScrollView
            ? new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            : new ScrollView
            {
                Content = column,
                ContentOrientation = ScrollingContentOrientation.Vertical,
                VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden,
                ZoomMode = ScrollingZoomMode.Disabled,
            };
    }

    protected void Section(string title) => Body.Children.Add(Ui.Text(title, "SectionStyle"));

    /// <summary>Runs an action with errors shown as a toast instead of crashing the page.</summary>
    protected async Task Try(string what, Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Main.Toast(what, ex.Message, InfoBarSeverity.Error); }
    }

    protected async Task<bool> Confirm(string title, string message, string yes)
    {
        var d = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, Content = Ui.Text(message, "MutedStyle"),
            PrimaryButtonText = yes, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        return await d.ShowAsync() == ContentDialogResult.Primary;
    }
}
