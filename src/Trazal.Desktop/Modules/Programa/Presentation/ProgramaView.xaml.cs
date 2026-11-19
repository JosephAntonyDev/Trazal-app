using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Trazal.Desktop.Modules.Programa.Presentation
{
    /// <summary>
    /// Code-behind de ProgramaView.
    /// Genera las columnas de mes dinámicamente cuando el ViewModel actualiza Months,
    /// ya que WPF DataGrid no soporta columnas bound a colecciones directamente.
    /// </summary>
    public partial class ProgramaView : UserControl
    {
        private ProgramaViewModel _vm;

        public ProgramaView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
                _vm.Months.CollectionChanged -= OnMonthsChanged;

            _vm = e.NewValue as ProgramaViewModel;

            if (_vm != null)
            {
                _vm.Months.CollectionChanged += OnMonthsChanged;
                RebuildMonthColumns();
            }
        }

        private void OnMonthsChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RebuildMonthColumns();
        }

        private DataGrid FindDataGrid()
        {
            // Use x:Name="ConceptGrid" directly after InitializeComponent
            return ConceptGrid;
        }

        private void RebuildMonthColumns()
        {
            var grid = FindDataGrid();
            if (grid == null || _vm == null) return;

            // Remove old month columns (between index 2 and the last "Reprogramar" column)
            var colCount = grid.Columns.Count;
            // Keep first 2 (Concepto, Inicio/término) and last 1 (Reprogramar)
            while (grid.Columns.Count > 3)
                grid.Columns.RemoveAt(2);

            // Insert month columns before "Reprogramar" (last column)
            int insertAt = grid.Columns.Count - 1; // before Reprogramar
            foreach (var mc in _vm.Months)
            {
                var col = BuildMonthColumn(mc);
                grid.Columns.Insert(insertAt, col);
                insertAt++;
            }
        }

        private static DataGridTemplateColumn BuildMonthColumn(MonthColumn mc)
        {
            var col = new DataGridTemplateColumn
            {
                Header    = mc.Header,
                Width     = new DataGridLength(145),
                MinWidth  = 110,
                CanUserSort = true
            };

            // CellTemplate: importe + barra proporcional con accent color
            var factory = new FrameworkElementFactory(typeof(StackPanel));
            factory.SetValue(StackPanel.MarginProperty, new Thickness(6, 4, 6, 4));

            // Amount text
            var txt = new FrameworkElementFactory(typeof(TextBlock));
            txt.SetValue(TextBlock.FontSizeProperty, 13.0);
            txt.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 0, 3));

            // Binding via converter that receives the row and extracts the right month
            var binding = new Binding(".")
            {
                Converter = new MonthAmountTextConverter(mc.Year, mc.Month)
            };
            txt.SetBinding(TextBlock.TextProperty, binding);

            // Progress bar (accent color strip)
            var bar = new FrameworkElementFactory(typeof(Border));
            bar.SetValue(Border.HeightProperty, 3.0);
            bar.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            var barBinding = new Binding(".")
            {
                Converter = new MonthBarWidthConverter(mc.Year, mc.Month)
            };
            bar.SetBinding(Border.WidthProperty, barBinding);
            bar.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xCC, 0xAA, 0x00)));
            bar.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);

            factory.AppendChild(txt);
            factory.AppendChild(bar);

            col.CellTemplate = new DataTemplate { VisualTree = factory };
            return col;
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }

    // ── Converters ───────────────────────────────────────────────────────────

    /// <summary>Devuelve el importe formateado en dinero para un mes dado, o "—" si es 0.</summary>
    public sealed class MonthAmountTextConverter : IValueConverter
    {
        private readonly int _year, _month;
        public MonthAmountTextConverter(int year, int month) { _year = year; _month = month; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is ProgramaRow row)
            {
                var amt = row.AmountForMonth(_year, _month);
                return amt == 0 ? "—" : amt.ToString("C0", CultureInfo.GetCultureInfo("es-MX"));
            }
            return "—";
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Devuelve el ancho de la barra proporcional (máx 100 px) según el importe del mes.</summary>
    public sealed class MonthBarWidthConverter : IValueConverter
    {
        private readonly int _year, _month;
        public MonthBarWidthConverter(int year, int month) { _year = year; _month = month; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is ProgramaRow row && row.Concept.Amount > 0)
            {
                var fraction = (double)(row.AmountForMonth(_year, _month) / row.Concept.Amount);
                return Math.Min(fraction, 1.0) * 100.0;
            }
            return 0.0;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Placeholder converter declarado en XAML Resources (no se usa en runtime).</summary>
    public sealed class MonthAmountConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => v;
    }
}
