using System;
using System.Windows;
using System.Windows.Controls;

namespace Trazal.Desktop
{
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
