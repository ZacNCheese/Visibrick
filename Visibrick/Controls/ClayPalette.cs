namespace Visibrick.Controls;

// Reads the clay colors from Colors.xaml for the current light/dark theme,
// so the drawn controls use the same palette as the XAML styles.
internal static class ClayPalette
{
    public static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    public static Color Get(string lightKey, string darkKey)
    {
        var key = IsDark ? darkKey : lightKey;

        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
            return color;

        return Colors.Transparent;
    }
}
