using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Trazal.Identity.Application;
using Trazal.Identity.Domain;

namespace Trazal.Desktop.Modules.Identity.Presentation
{
    public sealed class AccountViewModel : Observable
    {
        private readonly AccountSession session;
        private readonly IGoogleAuthorization google;
        private CancellationTokenSource googleCancellation;
        public bool GoogleConfigured => Configured && google != null;
        public string GoogleHint => GoogleConfigured ? "Se abrirá tu navegador para elegir tu cuenta." : "Google estará disponible al configurar el cliente de escritorio.";
        public bool WaitingForGoogle => googleCancellation != null;
        private readonly Action changed;
        private readonly Action<bool> setWorkspaceBusy;
        private string mode = "login", email = "", password = "", confirmation = "", message = "";
        private bool working, remember;
        public event Action ClearPasswords;
        public bool Configured { get; }
        public bool PendingConfiguration => !Configured;
        public string ConfigurationMessage { get; }
        public string AccountEmail => session?.Current?.Email ?? "";
        public bool SignedIn => session?.Current != null;
        public bool SignedOut => !SignedIn;
        public bool IsRegistration => mode == "register";
        public bool HasPassword => mode != "reset";
        public bool IsWorking => working;
        public bool CanEdit => !working;
        public bool Remember { get => remember; set => Set(ref remember, value); }
        public string Email { get => email; set { Set(ref email, value); RefreshCommands(); } }
        public string Password { set { password = value ?? ""; RefreshCommands(); } }
        public string Confirmation { set { confirmation = value ?? ""; RefreshCommands(); } }
        public string Message { get => message; private set => Set(ref message, value); }
        public string Title => SignedIn ? "Mi cuenta" : IsRegistration ? "Crear una cuenta" : mode == "reset" ? "Recuperar acceso" : "Iniciar sesión";
        public string SubmitLabel => working ? "Un momento…" : IsRegistration ? "Crear cuenta" : mode == "reset" ? "Enviar enlace" : "Iniciar sesión";
        public string SessionStatus => session?.NeedsOnlineCheck == true ? "Sesión pendiente de comprobar en línea"
            : session?.Current?.EmailVerified == true ? "Correo confirmado" : "Confirma tu correo para habilitar la sincronización";
        public ICommand LoginMode { get; }
        public ICommand RegisterMode { get; }
        public ICommand ResetMode { get; }
        public ICommand Submit { get; }
        public ICommand SignOut { get; }
        public ICommand Verify { get; }
        public ICommand RefreshIdentity { get; }
        public ICommand Back { get; }
        public ICommand GoogleSignIn { get; }
        public ICommand CancelGoogle { get; }

        public AccountViewModel(AccountSession session, bool configured, string configurationMessage, Action changed, Action back, Action<bool> setWorkspaceBusy = null, IGoogleAuthorization google = null)
        {
            this.google = google;
            this.session = session; this.changed = changed; this.setWorkspaceBusy = setWorkspaceBusy; Configured = configured; ConfigurationMessage = configurationMessage;
            LoginMode = new ActionCommand(_ => Switch("login"), _ => !working);
            RegisterMode = new ActionCommand(_ => Switch("register"), _ => !working);
            ResetMode = new ActionCommand(_ => Switch("reset"), _ => !working);
            Submit = new ActionCommand(async _ => await Run(SubmitAsync), _ => CanSubmit());
            SignOut = new ActionCommand(async _ => await Run(async () => { await session.SignOutAsync(CancellationToken.None); changed(); }), _ => SignedIn && !working);
            Verify = new ActionCommand(async _ => await Run(async () => { await session.SendVerificationAsync(CancellationToken.None); Message = "Enlace enviado. Revisa tu correo y la carpeta de spam."; }), _ => SignedIn && !working);
            RefreshIdentity = new ActionCommand(async _ => await Run(async () =>
            {
                await session.CheckIdentityAsync(CancellationToken.None);
                changed();
            }), _ => SignedIn && !working);
            Back = new ActionCommand(_ => back(), _ => !working);
            GoogleSignIn = new ActionCommand(async _ => await Run(async () =>
            {
                using (googleCancellation = new CancellationTokenSource())
                {
                    Notify(nameof(WaitingForGoogle)); RefreshCommands();
                    Message = "Completa el acceso en tu navegador. Después volverás a tu espacio en Trazal.";
                    try { await session.AuthenticateGoogleAsync(google, remember, googleCancellation.Token); changed(); }
                    finally { googleCancellation = null; Notify(nameof(WaitingForGoogle)); }
                }
            }), _ => GoogleConfigured && SignedOut && !working);
            CancelGoogle = new ActionCommand(_ => googleCancellation?.Cancel(), _ => WaitingForGoogle);
        }

        private bool CanSubmit() => Configured && !working && SignedOut && Credentials.IsEmail(email)
            && (!HasPassword || (!string.IsNullOrEmpty(password) && password.Length <= 4096))
            && (!IsRegistration || (password.Length >= 8 && password == confirmation));

        private void Switch(string next)
        {
            mode = next; Message = ""; ClearSecretFields();
            Notify(nameof(IsRegistration)); Notify(nameof(HasPassword)); Notify(nameof(Title)); Notify(nameof(SubmitLabel));
        }
        private async Task SubmitAsync()
        {
            if (!HasPassword)
            {
                await session.ResetPasswordAsync(email, CancellationToken.None);
                Message = "Si el correo tiene una cuenta, recibirás un enlace para restablecer la contraseña.";
                return;
            }
            await session.AuthenticateAsync(email, password, IsRegistration, remember, CancellationToken.None);
            changed();
        }
        private async Task Run(Func<Task> action)
        {
            if (working) return;
            working = true; Message = ""; UpdateWorking();
            try { await action(); }
            catch (OperationCanceledException) { Message = "Acceso con Google cancelado. Puedes intentarlo de nuevo."; }
            catch (IdentityException ex) { Message = ex.Message; }
            catch (ArgumentException ex) { Message = ex.Message; }
            catch (Exception) { Message = "No se pudo completar la operación. Tus obras siguen guardadas en este equipo."; }
            finally { working = false; ClearSecretFields(); UpdateWorking(); Notify(nameof(SessionStatus)); }
        }
        private void ClearSecretFields() { password = ""; confirmation = ""; ClearPasswords?.Invoke(); RefreshCommands(); }
        private void UpdateWorking() { setWorkspaceBusy?.Invoke(working); Notify(nameof(IsWorking)); Notify(nameof(CanEdit)); Notify(nameof(SubmitLabel)); RefreshCommands(); }
        private static void RefreshCommands() => CommandManager.InvalidateRequerySuggested();
    }
}
