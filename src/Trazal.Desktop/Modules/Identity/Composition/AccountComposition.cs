using System;
using System.IO;
using Trazal.Data;
using Trazal.Identity.Application;
using Trazal.Identity.Adapters.Files;
using Trazal.Identity.Adapters.Firebase;
using Trazal.Identity.Adapters.Windows;

namespace Trazal.Desktop.Modules.Identity.Composition
{
    public sealed class AccountComposition : IDisposable
    {
        private readonly MainWindow window;
        private readonly string root;
        private readonly IdentityConfiguration config;
        private readonly FirebaseIdentityProvider provider;
        private readonly AccountSession session;
        private CloudClient cloud;
        private readonly string configurationMessage = "El acceso con cuenta estará disponible cuando conectemos Firebase. Puedes seguir trabajando en tu espacio local.";

        public AccountComposition(MainWindow window, string root)
        {
            this.window = window; this.root = root;
            try { config = IdentityConfiguration.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "authsettings.json")); }
            catch (Exception) { config = new IdentityConfiguration(); configurationMessage = "No se pudo leer la configuración de acceso. Revisa authsettings.json; tus obras locales siguen disponibles."; }
            if (config.IsConfigured)
            {
                provider = new FirebaseIdentityProvider(config.ApiKey);
                session = new AccountSession(provider, new ProtectedSessionStore(Path.Combine(root, "identity", config.ProjectId + ".session"), config.ProjectId), new SystemClock());
                try { session.RestoreLocalSession(); }
                catch (Exception) { /* Local works remain accessible; the user can authenticate again. */ }
            }
        }
        public void Open(bool showAccount = false)
        {
            var identity = session?.Current;
            var path = identity == null ? Path.Combine(root, "trazal.db") : AccountWorkspace.DatabasePath(root, config.ProjectId, identity.Uid);
            var store = new LocalStore(path);
            var shell = new ShellViewModel(store);
            shell.ConfigureAccount(session, config.IsConfigured, configurationMessage, () => Open(true));
            cloud?.Dispose(); cloud = null;
            if (identity != null && identity.EmailVerified && !session.NeedsOnlineCheck && !string.IsNullOrWhiteSpace(config.ApiOrigin))
            {
                // The local identity is checked by Firebase; the Worker independently verifies each ID token.
                cloud = new CloudClient(config.ApiOrigin, session.GetIdTokenAsync);
                shell.ConfigureSync(cloud);
            }
            Theme.Apply(store.GetSetting("theme", "Sistema"));
            window.DataContext = shell;
            if (showAccount) shell.ShowAccount.Execute(null);
        }
        public void Dispose() { cloud?.Dispose(); provider?.Dispose(); }
    }
}
