using Avalonia;
using Avalonia.Controls;

namespace FlowRuns.Behaviors;

/// <summary>
/// Drives the run-detail drawer's open/close motion. Setting <c>Drawer.IsOpen</c>
/// shows the element and adds the <c>open</c> class so styles animate it in.
/// Clearing it removes the class, then hides the element only once its Opacity
/// has faded to 0 — so the exit can play, yet a closed drawer never keeps
/// keyboard focus. The element's style must start it hidden (IsVisible=False,
/// Opacity=0).
/// </summary>
/// <remarks>
/// Kept local to the plugin (mirrors the host's Sheet behavior) so the drawer
/// works on any host version, including ones without the shared motion styles.
/// </remarks>
public static class Drawer
{
    public static readonly AttachedProperty<bool> IsOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsOpen", typeof(Drawer));

    public static bool GetIsOpen(Control element) => element.GetValue(IsOpenProperty);
    public static void SetIsOpen(Control element, bool value) => element.SetValue(IsOpenProperty, value);

    static Drawer()
    {
        IsOpenProperty.Changed.AddClassHandler<Control>(OnIsOpenChanged);
        Visual.OpacityProperty.Changed.AddClassHandler<Control>((element, _) => HideIfFaded(element));
    }

    private static void OnIsOpenChanged(Control element, AvaloniaPropertyChangedEventArgs e)
    {
        var isOpen = e.GetNewValue<bool>();
        if (isOpen)
            element.IsVisible = true;

        element.Classes.Set("open", isOpen);
        HideIfFaded(element);
    }

    private static void HideIfFaded(Control element)
    {
        if (element.IsSet(IsOpenProperty) && !GetIsOpen(element) && element.IsVisible && element.Opacity <= 0)
            element.IsVisible = false;
    }
}
