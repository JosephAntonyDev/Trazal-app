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
        private Modules.Identity.Composition.AccountComposition accounts;
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
                var window = new MainWindow();
                accounts = new Modules.Identity.Composition.AccountComposition(window, folder);
                accounts.Open();
                MainWindow = window;
                MainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo abrir Trazal. Tus archivos no se han reemplazado.\n\n" + ex.Message, "Trazal · Inicio", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
        protected override void OnExit(ExitEventArgs e) { accounts?.Dispose(); instance?.Dispose(); Theme.Detach(); base.OnExit(e); }
    }
}
