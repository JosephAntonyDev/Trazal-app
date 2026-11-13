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
            SizeChanged += (_, __) =>
            {
                bool compact = ActualWidth < 1050;
                NavColumn.Width = new GridLength(compact ? 0 : 218);
                CompactBackup.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
                WelcomeText.Visibility = ActualWidth < 1000 ? Visibility.Collapsed : Visibility.Visible;
            };
            DataContextChanged += (_, e) => { if (e.OldValue is ShellViewModel old) old.PropertyChanged -= OnChanged; if (e.NewValue is ShellViewModel next) next.PropertyChanged += OnChanged; };
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && DataContext is ShellViewModel s && s.Dialog != null) { s.Dialog.Cancel.Execute(null); e.Handled = true; } };
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
