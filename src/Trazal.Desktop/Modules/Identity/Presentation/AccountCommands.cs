using System;
using System.Windows.Input;
using Trazal.Identity.Application;
using Trazal.Desktop.Modules.Identity.Presentation;

namespace Trazal.Desktop
{
    public sealed partial class ShellViewModel
    {
        private AccountSession accountSession;
        private bool accountConfigured;
        private string accountConfigurationMessage = "El acceso con cuenta estará disponible cuando conectemos Firebase. Puedes seguir trabajando en tu espacio local.";
        private Action accountChanged;
        public ICommand ShowAccount { get; private set; }
        public bool IsAccountSection => Page is AccountViewModel;
        public void ConfigureAccount(AccountSession session, bool configured, string configurationMessage, Action changed)
        {
            accountSession = session; accountConfigured = configured; accountConfigurationMessage = configurationMessage; accountChanged = changed;
        }
        private void OpenAccount()
        {
            Page = new AccountViewModel(accountSession, accountConfigured, accountConfigurationMessage, () => accountChanged?.Invoke(), OpenHome, value => busy = value);
            Location = "Mi cuenta";
        }
    }
}
