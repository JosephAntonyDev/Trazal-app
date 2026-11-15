using System.Windows;
using System.Windows.Controls;

namespace Trazal.Desktop.Modules.Identity.Presentation
{
    public partial class AccountView : UserControl
    {
        public AccountView()
        {
            InitializeComponent();
            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is AccountViewModel old) old.ClearPasswords -= Clear;
                if (e.NewValue is AccountViewModel next) next.ClearPasswords += Clear;
                Clear();
            };
            Unloaded += (_, __) => Clear();
        }
        private void Clear() { PasswordInput.Clear(); ConfirmationInput.Clear(); }
        private void PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is AccountViewModel vm) vm.Password = PasswordInput.Password;
            if (PasswordHint != null) PasswordHint.Visibility = PasswordInput.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        private void ConfirmationChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is AccountViewModel vm) vm.Confirmation = ConfirmationInput.Password;
            if (ConfirmationHint != null) ConfirmationHint.Visibility = ConfirmationInput.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
