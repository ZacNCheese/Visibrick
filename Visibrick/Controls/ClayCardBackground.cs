namespace Visibrick.Controls;

// Draws a clay card (soft drop shadow, gradient fill, bright rim) behind a list item's content.
//
// List cards use this instead of a Border with the ClayCard style: on Windows, many Borders inside a
// CollectionView can crash with "Layout cycle detected" when the list re-lays out (e.g. on resize).
// Put it first in a Grid and give the content a Margin of CardInset + your padding.
public class ClayCardBackground : GraphicsView
{
    // Room around the card for its shadow, which falls downward
    public static readonly Thickness CardInset = new(8, 10, 8, 14);

    private const float CornerRadius = 28;

    public ClayCardBackground()
    {
        Drawable = new CardDrawable();
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;

        // Redraw in the new palette when the user switches between light and dark mode
        Loaded += (_, _) => { if (Application.Current is { } app) app.RequestedThemeChanged += OnThemeChanged; };
        Unloaded += (_, _) => { if (Application.Current is { } app) app.RequestedThemeChanged -= OnThemeChanged; };
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Invalidate();

    private class CardDrawable : IDrawable
    {
        public void Draw(ICanvas canvas, RectF bounds)
        {
            var card = new RectF(
                bounds.X + (float)CardInset.Left,
                bounds.Y + (float)CardInset.Top,
                bounds.Width - (float)CardInset.HorizontalThickness,
                bounds.Height - (float)CardInset.VerticalThickness);

            if (card.Width <= 0 || card.Height <= 0)
                return;

            var surface = ClayPalette.Get("ClaySurface", "ClaySurfaceDark");
            var shade = ClayPalette.Get("ClaySurfaceShade", "ClaySurfaceShadeDark");
            var shadow = ClayPalette.Get("ClayShadow", "ClayShadow");

            // Soft purple drop shadow
            canvas.SaveState();
            canvas.SetShadow(new SizeF(0, 8), 18, shadow.WithAlpha(0.28f));
            canvas.FillColor = surface;
            canvas.FillRoundedRectangle(card, CornerRadius);
            canvas.RestoreState();

            // Gradient fill: lighter at the top-left, slightly deeper at the bottom-right
            canvas.SetFillPaint(new LinearGradientPaint(
                [new PaintGradientStop(0, surface), new PaintGradientStop(1, shade)],
                new Point(0, 0), new Point(1, 1)), card);
            canvas.FillRoundedRectangle(card, CornerRadius);

            // Rim: a bright edge that fades out, which gives the "puffy" look
            canvas.SetFillPaint(new LinearGradientPaint(
                [
                    new PaintGradientStop(0, Colors.White.WithAlpha(ClayPalette.IsDark ? 0.22f : 0.95f)),
                    new PaintGradientStop(0.55f, Colors.White.WithAlpha(0)),
                    new PaintGradientStop(1, (ClayPalette.IsDark ? Colors.Black : shadow).WithAlpha(0.25f))
                ],
                new Point(0, 0), new Point(1, 1)), card);

            var rim = new PathF();
            rim.AppendRoundedRectangle(card, CornerRadius);
            rim.AppendRoundedRectangle(card.Inflate(-2, -2), CornerRadius - 2);
            canvas.FillPath(rim, WindingMode.EvenOdd);
        }
    }
}
