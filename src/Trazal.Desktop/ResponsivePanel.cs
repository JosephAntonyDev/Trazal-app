using System;
using System.Windows;
using System.Windows.Controls;

namespace Trazal.Desktop
{
    // Data grids need a bounded viewport; a short window scrolls the page instead of clipping its actions.
    public sealed class PageMinimumHeightConverter : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var page = values.Length > 0 ? values[0] : null;
            var narrow = values.Length < 2 || !(values[1] is double width) || width < 850;
            if (page is WorkViewModel) return narrow ? 780d : 560d;
            return page is ProgramViewModel || page is ExpensesViewModel || page is SyncViewModel || page is ImportViewModel
                ? (narrow ? 640d : 480d) : 0d;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    // Equal-width cards/fields, with no fixed empty gutter at intermediate window sizes.
    public sealed class ResponsivePanel : Panel
    {
        public int ColumnsLimit { get; set; } = 2;
        public double MinimumItemWidth { get; set; } = 310;
        public double Gap { get; set; } = 22;
        private int Columns(double width) => Math.Max(1, Math.Min(ColumnsLimit, (int)((width + Gap) / (MinimumItemWidth + Gap))));
        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsInfinity(availableSize.Width) ? MinimumItemWidth : availableSize.Width;
            var columns = Columns(width);
            var itemWidth = Math.Max(0, (width - Gap * (columns - 1)) / columns);
            double totalHeight = 0, rowHeight = 0;
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                var child = InternalChildren[i]; child.Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                if (i % columns == columns - 1 || i == InternalChildren.Count - 1) { totalHeight += rowHeight + Gap; rowHeight = 0; }
            }
            return new Size(width, Math.Max(0, totalHeight - Gap));
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            int columns = Columns(finalSize.Width);
            var width = Math.Max(0, (finalSize.Width - Gap * (columns - 1)) / columns);
            double top = 0;
            for (int start = 0; start < InternalChildren.Count; start += columns)
            {
                double height = 0;
                for (int j = start; j < Math.Min(start + columns, InternalChildren.Count); j++) height = Math.Max(height, InternalChildren[j].DesiredSize.Height);
                for (int j = start; j < Math.Min(start + columns, InternalChildren.Count); j++) InternalChildren[j].Arrange(new Rect((j - start) * (width + Gap), top, width, height));
                top += height + Gap;
            }
            return finalSize;
        }
    }
}
