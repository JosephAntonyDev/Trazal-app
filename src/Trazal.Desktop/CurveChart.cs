using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Trazal.Core;

namespace Trazal.Desktop
{
    // Native retained drawing, with no browser/chart runtime. Measurements are steps, never fabricated interpolation.
    public sealed class CurveChart : FrameworkElement
    {
        public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(nameof(Snapshot), typeof(PerformanceSnapshot), typeof(CurveChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public PerformanceSnapshot Snapshot { get => (PerformanceSnapshot)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
        public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(CurveChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }
        private double WidthPlot => Math.Max(1, ActualWidth - 128);
        private double HeightPlot => Math.Max(1, ActualHeight - 68);
        private double X(DateTime date) => 100 + (date - Snapshot.Points[0].Date).TotalDays / Math.Max(1, (Snapshot.Points.Last().Date - Snapshot.Points[0].Date).TotalDays) * WidthPlot;
        private decimal Max = 1m;
        private double Y(decimal value) => 20 + HeightPlot * (1 - (double)(value / Max));
        private void Label(DrawingContext dc, string value, double x, double y, double size = 12)
        { dc.DrawText(new FormattedText(value, CultureInfo.GetCultureInfo("es-MX"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y)); }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            if (Snapshot == null || ActualWidth < 200) { Label(dc, "Selecciona una fecha de corte.", 16, 20); return; }
            Max = Math.Max(1m, Snapshot.Points.Max(p => Math.Max(p.Planned, Math.Max(p.Earned ?? 0, p.Spent ?? 0))) * 1.1m);
            for (int i = 0; i <= 4; i++)
            {
                var value = Max * i / 4; var y = Y(value);
                dc.DrawLine(new Pen(Ink, 0.3), new Point(100, y), new Point(100 + WidthPlot, y));
                Label(dc, value >= 1000000 ? (value / 1000000).ToString("0.##") + " M" : value >= 1000 ? (value / 1000).ToString("0.#") + " mil" : value.ToString("0.##"), 8, y - 8);
            }
            Label(dc, "MXN sin IVA", 8, 0);
            for (int i = 0; i <= 3; i++)
            {
                var date = Snapshot.Points[0].Date.AddDays((Snapshot.Points.Last().Date - Snapshot.Points[0].Date).TotalDays * i / 3);
                Label(dc, date.ToString("dd/MM/yy"), X(date) - 28, 28 + HeightPlot);
            }
            Line(dc, p => p.Planned, Color.FromRgb(65, 151, 201), false);
            Line(dc, p => p.Earned, Color.FromRgb(140, 147, 153), true);
            Line(dc, p => p.Spent, Color.FromRgb(222, 99, 84), true);
            dc.DrawLine(new Pen(Ink, 1) { DashStyle = DashStyles.Dash }, new Point(X(Snapshot.Cut), 18), new Point(X(Snapshot.Cut), 20 + HeightPlot));
        }
        private void Line(DrawingContext dc, Func<CurvePoint, decimal?> value, Color color, bool dashed)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                Point? previous = null;
                foreach (var row in Snapshot.Points)
                {
                    var v = value(row); if (!v.HasValue) break;
                    var point = new Point(X(row.Date), Y(v.Value));
                    if (previous == null) ctx.BeginFigure(point, false, false);
                    else { ctx.LineTo(new Point(point.X, previous.Value.Y), true, false); ctx.LineTo(point, true, false); }
                    previous = point;
                }
            }
            geometry.Freeze(); dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 2.5) { DashStyle = dashed ? (color.R == 140 ? DashStyles.Dash : DashStyles.Dot) : DashStyles.Solid }, geometry);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (Snapshot == null) return;
            var p = Snapshot.Points.OrderBy(row => Math.Abs(X(row.Date) - e.GetPosition(this).X)).First();
            ToolTip = p.Date.ToString("dd MMM yyyy") + "\nPrograma: " + ShellViewModel.Money(p.Planned) + "\nEjecutado: " + (p.Earned.HasValue ? ShellViewModel.Money(p.Earned.Value) : "Después del corte") + "\nGastos: " + (p.Spent.HasValue ? ShellViewModel.Money(p.Spent.Value) : "Después del corte");
        }
    }
}
