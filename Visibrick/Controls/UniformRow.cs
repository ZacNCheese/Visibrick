using Microsoft.Maui.Layouts;

namespace Visibrick.Controls;

// Lays its children out left to right in equal-width columns. A row with fewer children than
// Columns (the last row of a grid) keeps the same column width, so cards line up with the rows above.
public class UniformRow : Layout
{
    public static readonly BindableProperty ColumnsProperty = BindableProperty.Create(
        nameof(Columns), typeof(int), typeof(UniformRow), 1,
        propertyChanged: (bindable, _, _) => ((UniformRow)bindable).InvalidateMeasure());

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    protected override ILayoutManager CreateLayoutManager() => new UniformRowManager(this);

    private class UniformRowManager : LayoutManager
    {
        private readonly UniformRow _row;

        public UniformRowManager(UniformRow row) : base(row)
        {
            _row = row;
        }

        public override Size Measure(double widthConstraint, double heightConstraint)
        {
            var columnWidth = ColumnWidth(widthConstraint);
            double height = 0;

            foreach (var child in _row)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;

                height = Math.Max(height, child.Measure(columnWidth, double.PositiveInfinity).Height);
            }

            var width = double.IsInfinity(widthConstraint) ? columnWidth * _row.Count : widthConstraint;
            return new Size(width, height + _row.Padding.VerticalThickness);
        }

        public override Size ArrangeChildren(Rect bounds)
        {
            var columnWidth = ColumnWidth(bounds.Width);
            var x = bounds.X + _row.Padding.Left;
            var y = bounds.Y + _row.Padding.Top;
            var height = bounds.Height - _row.Padding.VerticalThickness;

            foreach (var child in _row)
            {
                if (child.Visibility == Visibility.Collapsed)
                    continue;

                child.Arrange(new Rect(x, y, columnWidth, height));
                x += columnWidth;
            }

            return bounds.Size;
        }

        private double ColumnWidth(double totalWidth)
        {
            // Unbounded width shouldn't happen inside a vertical list, but fall back to a sensible card size
            if (double.IsInfinity(totalWidth))
                return 180;

            return Math.Max(0, totalWidth - _row.Padding.HorizontalThickness) / Math.Max(1, _row.Columns);
        }
    }
}
