namespace Visibrick.Controls;

// Gives tappable views a tactile, clay-like response: they lift slightly when the pointer is over them,
// squish when pressed and spring back when released.
//
//   <Border controls:PressFeedback.IsEnabled="True"> ... </Border>
//   or in a Style: <Setter Property="controls:PressFeedback.IsEnabled" Value="True" />
public static class PressFeedback
{
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled", typeof(bool), typeof(PressFeedback), false,
        propertyChanged: (bindable, _, newValue) =>
        {
            if (bindable is View view && (bool)newValue)
                Attach(view);
        });

    public static bool GetIsEnabled(BindableObject view) => (bool)view.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(BindableObject view, bool value) => view.SetValue(IsEnabledProperty, value);

    private const double HoverScale = 1.02;
    private const double HoverLift = -2;
    private const double PressedScale = 0.96;
    private const double PressedOpacity = 0.88;

    private sealed class State
    {
        public bool Hovered;
        public bool Pressed;
    }

    private static void Attach(View view)
    {
        var state = new State();

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => { state.Hovered = true; Animate(view, state, released: false); };
        pointer.PointerExited += (_, _) => { state.Hovered = false; state.Pressed = false; Animate(view, state, released: false); };
        view.GestureRecognizers.Add(pointer);

        if (view is Button button)
        {
            // Buttons handle presses themselves, so listen to their own events (these also fire for touch)
            button.Pressed += (_, _) => { state.Pressed = true; Animate(view, state, released: false); };
            button.Released += (_, _) => { state.Pressed = false; Animate(view, state, released: true); };
        }
        else
        {
            pointer.PointerPressed += (_, _) => { state.Pressed = true; Animate(view, state, released: false); };
            pointer.PointerReleased += (_, _) => { state.Pressed = false; Animate(view, state, released: true); };
        }
    }

    private static void Animate(View view, State state, bool released)
    {
        view.CancelAnimations();

        if (!view.IsEnabled)
        {
            view.Scale = 1;
            view.TranslationY = 0;
            view.Opacity = 1;
            return;
        }

        var (scale, lift, opacity) = state switch
        {
            { Pressed: true } => (PressedScale, 0.0, PressedOpacity),
            { Hovered: true } => (HoverScale, HoverLift, 1.0),
            _ => (1.0, 0.0, 1.0)
        };

        // Squish quickly; spring back with a little bounce after a press; ease gently on hover
        var (length, easing) = state.Pressed ? (90u, Easing.CubicOut)
            : released ? (320u, Easing.SpringOut)
            : (150u, Easing.CubicOut);

        _ = view.ScaleTo(scale, length, easing);
        _ = view.TranslateTo(view.TranslationX, lift, length, easing);
        _ = view.FadeTo(opacity, length, easing);
    }
}
