using System.Numerics;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Hyprism.App;

/// <summary>
/// Press feedback for buttons and a hover lift for plain cards, wired once on the window root.
/// Buttons deliberately have no hover motion (just the standard hover color).
/// It only sets UIElement.Scale/Translation; the implicit Vector3Transitions animate those on the compositor
/// thread, so motion stays at the display's refresh rate even while the UI thread is busy.
/// Cards only lift (no scale): scaling text forces it to re-rasterize, which costs frames and blurs it.
/// </summary>
public static class Motion
{
    static readonly TimeSpan In = TimeSpan.FromMilliseconds(140);
    static readonly TimeSpan Out = TimeSpan.FromMilliseconds(240);
    static readonly TimeSpan Down = TimeSpan.FromMilliseconds(70);
    static readonly HashSet<FrameworkElement> hovered = [];
    static FrameworkElement? pressed;
    static object? lastSource;
    static Style? cardStyle;

    public static void Attach(UIElement root)
    {
        cardStyle = Ui.Style("CardStyle");
        // handledEventsToo: Buttons mark pointer events handled, but we still want to see them.
        root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        root.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((_, _) => { lastSource = null; Hover([]); }), true);
        root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => { lastSource = null; Hover([]); }), true);
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) => Press(Chain(e.OriginalSource).FirstOrDefault(fe => fe is ButtonBase or GridViewItem))), true);
        root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Release()), true);
        root.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => Release()), true);
        root.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => Release()), true);
    }

    static void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        // PointerMoved fires for every pixel of movement: only do work when the element under the pointer changes.
        if (ReferenceEquals(e.OriginalSource, lastSource)) return;
        lastSource = e.OriginalSource;
        Hover(Chain(e.OriginalSource));
    }

    /// <summary>Animatable elements under the pointer, innermost first (a button, then the card it sits on).</summary>
    static List<FrameworkElement> Chain(object source)
    {
        var list = new List<FrameworkElement>(2);
        for (var d = source as DependencyObject; d is not null and not NavigationView; d = VisualTreeHelper.GetParent(d))
            if (d is FrameworkElement fe && IsAnimatable(fe)) list.Add(fe);
        return list;
    }

    static bool IsAnimatable(FrameworkElement fe) => fe switch
    {
        HyperlinkButton => false,              // text links: the color change is enough
        SettingsCard => true,                  // (a ButtonBase too, but it's a card)
        ButtonBase => true,                    // Button, DropDownButton, ToggleButton...
        GridViewItem => true,
        Border b => ReferenceEquals(b.Style, cardStyle),
        _ => false,
    };

    static bool IsCard(FrameworkElement fe) => fe is SettingsCard or Border or GridViewItem || fe.ActualWidth > 360;

    static (float Scale, float Lift) HoverPose(FrameworkElement fe) => IsCard(fe) ? (1f, -2f) : (1.04f, -1f);

    static void Hover(List<FrameworkElement> chain)
    {
        foreach (var old in hovered.Where(h => !chain.Contains(h)).ToList())
        {
            hovered.Remove(old);
            if (old != pressed) Animate(old, 1f, 0f, Out);
        }
        // Buttons (incl. setting rows and tiles) get no hover motion, only Windows' own hover color; plain cards lift.
        foreach (var fe in chain.Where(fe => fe is not ButtonBase))
            if (hovered.Add(fe) && fe != pressed) { var (s, l) = HoverPose(fe); Animate(fe, s, l, In); }
    }

    static void Press(FrameworkElement? fe)
    {
        if (fe is null) return;
        pressed = fe;
        Animate(fe, IsCard(fe) ? 0.995f : 0.95f, 0f, Down);
    }

    static void Release()
    {
        if (pressed is not { } fe) return;
        pressed = null;
        var (s, l) = hovered.Contains(fe) && fe is not ButtonBase ? HoverPose(fe) : (1f, 0f);
        Animate(fe, s, l, Out);
    }

    static void Animate(FrameworkElement el, float scale, float lift, TimeSpan duration)
    {
        el.CenterPoint = new Vector3((float)el.ActualWidth / 2, (float)el.ActualHeight / 2, 0);
        // One transition object per element, retimed per gesture (quick in, softer settle).
        if (el.ScaleTransition is not { } st) el.ScaleTransition = st = new Vector3Transition();
        if (el.TranslationTransition is not { } tt) el.TranslationTransition = tt = new Vector3Transition();
        st.Duration = tt.Duration = duration;
        el.Scale = new Vector3(scale, scale, 1);
        el.Translation = new Vector3(0, lift, 0);
    }
}
