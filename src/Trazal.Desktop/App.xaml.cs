using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using Trazal.Data;

namespace Trazal.Desktop
{
    public partial class App : Application
    {
        private Mutex instance;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var culture = CultureInfo.GetCultureInfo("es-MX");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("es-MX")));
            try
            {
                var folder = Environment.GetEnvironmentVariable("TRAZAL_DATA_DIR");
                if (string.IsNullOrEmpty(folder)) folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Trazal", "Preview");
                bool first;
                instance = new Mutex(true, "Local\\Trazal.Desktop.Preview", out first);
                if (!first) { MessageBox.Show("Trazal ya está abierto. Revisa tu barra de tareas.", "Trazal"); Shutdown(); return; }
                var store = new LocalStore(Path.Combine(folder, "trazal.db"));
                Theme.Apply(store.GetSetting("theme", "Sistema"));
                var vm = new ShellViewModel(store);
                MainWindow = new MainWindow { DataContext = vm };
                MainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo abrir Trazal. Tus archivos no se han reemplazado.\n\n" + ex.Message, "Trazal · Inicio", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
        protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); Theme.Detach(); base.OnExit(e); }
    }
}
