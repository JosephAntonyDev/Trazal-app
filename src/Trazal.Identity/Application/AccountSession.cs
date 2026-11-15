using System;
using System.Threading;
using System.Threading.Tasks;
using Trazal.Identity.Domain;

namespace Trazal.Identity.Application
{
    // Use cases depend only on ports. No Firebase, WPF, SQL or file APIs here.
    public sealed class AccountSession
    {
        private readonly IIdentityProvider provider;
        private readonly ISessionStore storage;
        private readonly IClock clock;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private IdentityTokens tokens;
        private bool remember;
        public AccountIdentity Current => tokens?.Identity;
        public bool NeedsOnlineCheck { get; private set; }

        public AccountSession(IIdentityProvider provider, ISessionStore storage, IClock clock)
        {
            this.provider = provider; this.storage = storage; this.clock = clock;
        }

        // A protected local session opens only its own workspace offline. It never proves cloud access.
        public void RestoreLocalSession()
        {
            var saved = storage.Read();
            if (saved == null) return;
            if (string.IsNullOrWhiteSpace(saved.Uid) || saved.Uid.Length > 128 || string.IsNullOrEmpty(saved.RefreshToken))
                throw new IdentityException("session_invalid", "La sesión guardada no es válida. Inicia sesión de nuevo.");
            tokens = new IdentityTokens { Identity = new AccountIdentity { Uid = saved.Uid, Email = saved.Email }, RefreshToken = saved.RefreshToken };
            remember = true; NeedsOnlineCheck = true;
        }

        public async Task AuthenticateAsync(string email, string password, bool register, bool keepSession, CancellationToken cancellation)
        {
            Credentials.Validate(email, password, register);
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var next = register
                    ? await provider.RegisterAsync(email.Trim(), password, cancellation).ConfigureAwait(false)
                    : await provider.SignInAsync(email.Trim(), password, cancellation).ConfigureAwait(false);
                ValidateTokens(next);
                if (keepSession) Save(next); else storage.Clear();
                tokens = next; remember = keepSession; NeedsOnlineCheck = false;
            }
            finally { gate.Release(); }
        }

        public async Task AuthenticateGoogleAsync(IGoogleAuthorization authorization, bool keepSession, CancellationToken cancellation)
        {
            if (authorization == null || !(provider is IGoogleIdentityProvider))
                throw new IdentityException("configuration", "Falta configurar el acceso con Google para escritorio.");
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var credential = await authorization.AuthorizeAsync(cancellation).ConfigureAwait(false);
                var next = await ((IGoogleIdentityProvider)provider).SignInGoogleAsync(credential, cancellation).ConfigureAwait(false);
                ValidateTokens(next);
                cancellation.ThrowIfCancellationRequested();
                if (keepSession) Save(next); else storage.Clear();
                tokens = next; remember = keepSession; NeedsOnlineCheck = false;
            }
            finally { gate.Release(); }
        }

        public async Task<string> GetIdTokenAsync(CancellationToken cancellation)
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                await RenewIfNeeded(false, cancellation).ConfigureAwait(false);
                if (!tokens.Identity.EmailVerified)
                    throw new IdentityException("email_unverified", "Confirma tu correo y pulsa «Ya confirmé mi correo» en Mi cuenta.");
                return tokens.IdToken;
            }
            finally { gate.Release(); }
        }

        public async Task CheckIdentityAsync(CancellationToken cancellation)
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try { await RenewIfNeeded(true, cancellation).ConfigureAwait(false); }
            finally { gate.Release(); }
        }

        private async Task RenewIfNeeded(bool force, CancellationToken cancellation)
        {
            if (tokens == null) throw new IdentityException("sign_in_required", "Inicia sesión para continuar.");
            if (!force && !string.IsNullOrEmpty(tokens.IdToken) && tokens.ExpiresAtUtc > clock.UtcNow.AddMinutes(2)) return;
            try
            {
                var next = await provider.RefreshAsync(tokens.RefreshToken, cancellation).ConfigureAwait(false);
                ValidateTokens(next);
                if (next.Identity.Uid != tokens.Identity.Uid)
                    throw new IdentityException("identity_mismatch", "La sesión no corresponde a esta cuenta. Vuelve a iniciar sesión.");
                if (remember) Save(next);
                tokens = next; NeedsOnlineCheck = false;
            }
            catch (IdentityException ex) when (ex.Code == "session_expired" || ex.Code == "identity_mismatch")
            {
                // Keep the local workspace open, but never use the rejected credential again.
                tokens.IdToken = null; tokens.RefreshToken = null; NeedsOnlineCheck = true;
                storage.Clear(); throw;
            }
        }

        public async Task SendVerificationAsync(CancellationToken cancellation)
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try { await RenewIfNeeded(false, cancellation).ConfigureAwait(false); await provider.SendVerificationAsync(tokens.IdToken, cancellation).ConfigureAwait(false); }
            finally { gate.Release(); }
        }

        public Task ResetPasswordAsync(string email, CancellationToken cancellation)
        {
            if (!Credentials.IsEmail(email)) throw new ArgumentException("Escribe un correo electrónico válido.");
            return provider.SendPasswordResetAsync(email.Trim(), cancellation);
        }

        public async Task SignOutAsync(CancellationToken cancellation)
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try { storage.Clear(); tokens = null; remember = false; NeedsOnlineCheck = false; }
            finally { gate.Release(); }
        }

        private void Save(IdentityTokens next) => storage.Save(new RememberedSession
            { Uid = next.Identity.Uid, Email = next.Identity.Email, RefreshToken = next.RefreshToken });
        private static void ValidateTokens(IdentityTokens next)
        {
            if (next?.Identity == null || string.IsNullOrWhiteSpace(next.Identity.Uid) || next.Identity.Uid.Length > 128
                || string.IsNullOrEmpty(next.IdToken) || string.IsNullOrEmpty(next.RefreshToken))
                throw new IdentityException("invalid_response", "No se pudo comprobar la sesión.");
        }
    }
}
