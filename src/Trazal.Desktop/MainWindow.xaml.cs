using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Trazal.Desktop
{
    public partial class MainWindow : Window
    {
        private IInputElement previousFocus;
        public MainWindow()
        {
            InitializeComponent();
            // WorkArea is expressed in WPF device-independent units at the system DPI.
            var area = SystemParameters.WorkArea;
            MinWidth = System.Math.Min(MinWidth, area.Width);
            MinHeight = System.Math.Min(MinHeight, area.Height);
            Width = System.Math.Min(Width, area.Width);
            Height = System.Math.Min(Height, area.Height);
            SizeChanged += (_, __) => UpdateAdaptiveLayout(ActualWidth);
            DataContextChanged += (_, e) => { if (e.OldValue is ShellViewModel old) { old.PropertyChanged -= OnChanged; old.Dispose(); } if (e.NewValue is ShellViewModel next) next.PropertyChanged += OnChanged; };
            Closed += (_, __) => (DataContext as ShellViewModel)?.Dispose();
            Closing += (_, e) => { if (DataContext is ShellViewModel shell && shell.IsBusy) e.Cancel = true; };
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && DataContext is ShellViewModel s && s.Dialog != null) { s.Dialog.Cancel.Execute(null); e.Handled = true; } };
        }
        private void UpdateAdaptiveLayout(double width)
        {
            bool compact = width < 1050;
            NavColumn.Width = new GridLength(compact ? 0 : 218);
            NavigationPane.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            CompactNavigation.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            WelcomeText.Visibility = width < 1000 ? Visibility.Collapsed : Visibility.Visible;
        }
        private void Navigation_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu.IsOpen = true;
        }
        private void OnChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "DialogOpen") return;
            if (((ShellViewModel)sender).DialogOpen)
            {
                previousFocus = Keyboard.FocusedElement;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new System.Action(() => { var input = FindTextBox(ModalLayer); input?.Focus(); }));
            }
            else if (previousFocus != null) Dispatcher.BeginInvoke(DispatcherPriority.Input, new System.Action(() => Keyboard.Focus(previousFocus)));
        }
        private static TextBox FindTextBox(DependencyObject node)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is TextBox input && input.IsVisible) return input;
                var found = FindTextBox(child); if (found != null) return found;
            }
            return null;
        }
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var tip = button.ToolTip as ToolTip;
            if (tip == null) { tip = new ToolTip { Content = button.ToolTip is string text ? new TextBlock { Text = text } : button.ToolTip }; button.ToolTip = tip; }
            tip.PlacementTarget = button; tip.StaysOpen = false; tip.IsOpen = true;
        }
    }
}
