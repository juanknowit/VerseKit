using Avalonia;
using Avalonia.Controls;

namespace VerseKit.App.Behaviors;

/// <summary>
/// Drives a modal overlay's open/close motion (see "Motion" in DESIGN.md).
/// Setting <c>Sheet.IsOpen</c> shows the element and adds the <c>open</c> class
/// so styles animate it in. Clearing it removes the class, then hides the
/// element only once its Opacity has faded to 0 — so the exit animation can
/// play, yet a closed sheet never keeps keyboard focus or stays tabbable.
/// The element's style must start it hidden (IsVisible=False, Opacity=0).
/// </summary>
public static class Sheet
{
    public static readonly AttachedProperty<bool> IsOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsOpen", typeof(Sheet));

    public static bool GetIsOpen(Control element) => element.GetValue(IsOpenProperty);
    public static void SetIsOpen(Control element, bool value) => element.SetValue(IsOpenProperty, value);

    static Sheet()
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

    // Also covers the no-transition case (Opacity drops to 0 immediately).
    private static void HideIfFaded(Control element)
    {
        if (element.IsSet(IsOpenProperty) && !GetIsOpen(element) && element.IsVisible && element.Opacity <= 0)
            element.IsVisible = false;
    }
}
