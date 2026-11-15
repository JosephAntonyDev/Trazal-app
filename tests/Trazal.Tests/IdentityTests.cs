using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trazal.Identity.Application;
using Trazal.Identity.Domain;
using Trazal.Identity.Adapters.Firebase;
using Trazal.Identity.Adapters.Files;
using Trazal.Identity.Adapters.Windows;
using Trazal.Desktop.Modules.Identity.Presentation;

namespace Trazal.Tests
{
    internal static partial class Program
    {
        private sealed class IdentityClock : IClock { public DateTime Now = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc); public DateTime UtcNow => Now; }
        private sealed class MemorySession : ISessionStore
        {
            public RememberedSession Saved;
            public RememberedSession Read() => Saved;
            public void Save(RememberedSession value) { Saved = value; }
            public void Clear() { Saved = null; }
        }
        private sealed class FakeIdentity : IIdentityProvider
        {
            public string Uid = "account-one";
            public bool Verified = true, RejectRefresh;
            public int RefreshCalls, LoginCalls;
            public readonly IdentityClock Clock = new IdentityClock();
            private IdentityTokens Tokens() => new IdentityTokens
            {
                Identity = new AccountIdentity { Uid = Uid, Email = "test@example.com", EmailVerified = Verified },
                IdToken = "id-token", RefreshToken = "refresh-token", ExpiresAtUtc = Clock.UtcNow.AddHours(1)
            };
            public Task<IdentityTokens> SignInAsync(string email, string password, CancellationToken cancellation) { LoginCalls++; return Task.FromResult(Tokens()); }
            public Task<IdentityTokens> RegisterAsync(string email, string password, CancellationToken cancellation) => SignInAsync(email, password, cancellation);
            public async Task<IdentityTokens> RefreshAsync(string token, CancellationToken cancellation)
            {
                RefreshCalls++; await Task.Delay(5, cancellation);
                if (RejectRefresh) throw new IdentityException("session_expired", "Vuelve a iniciar sesión.");
                return Tokens();
            }
            public Task<AccountIdentity> GetIdentityAsync(string token, CancellationToken cancellation) => Task.FromResult(Tokens().Identity);
            public Task SendVerificationAsync(string token, CancellationToken cancellation) => Task.CompletedTask;
            public Task SendPasswordResetAsync(string email, CancellationToken cancellation) => Task.CompletedTask;
        }
        private static void Login(AccountSession session, bool remember = false) => session.AuthenticateAsync("test@example.com", "test-password", false, remember, CancellationToken.None).GetAwaiter().GetResult();
        private static void IdentityReject(string code, Func<Task> action)
        {
            try { action().GetAwaiter().GetResult(); }
            catch (IdentityException ex) { Equal(code, ex.Code); return; }
            throw new Exception("Se esperaba rechazo de identidad: " + code);
        }
        private sealed class FirebaseHttp : HttpMessageHandler
        {
            public readonly Queue<string> Replies = new Queue<string>();
            public readonly List<string> Paths = new List<string>();
            public readonly List<string> Bodies = new List<string>();
            public HttpStatusCode Status = HttpStatusCode.OK;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Paths.Add(request.RequestUri.AbsoluteUri);
                Bodies.Add(await request.Content.ReadAsStringAsync());
                Equal("https", request.RequestUri.Scheme);
                return new HttpResponseMessage(Status) { Content = new StringContent(Replies.Dequeue(), Encoding.UTF8, "application/json") };
            }
        }
        private static void IdentityTests(string root)
        {
            Check("Login valida antes de contactar al proveedor", () =>
            {
                var provider = new FakeIdentity(); var session = new AccountSession(provider, new MemorySession(), provider.Clock);
                Reject(() => session.AuthenticateAsync("sin-correo", "test-password", false, false, CancellationToken.None).GetAwaiter().GetResult());
                Reject(() => session.AuthenticateAsync("a@b.com", "corta", true, false, CancellationToken.None).GetAwaiter().GetResult());
                Equal(0, provider.LoginCalls);
            });
            Check("Sesión sin recordar no persiste credenciales", () =>
            {
                var provider = new FakeIdentity(); var storage = new MemorySession(); var session = new AccountSession(provider, storage, provider.Clock);
                Login(session); Equal(null, storage.Saved); Equal("account-one", session.Current.Uid);
            });
            Check("Renovaciones simultáneas utilizan una sola petición", () =>
            {
                var provider = new FakeIdentity(); var session = new AccountSession(provider, new MemorySession(), provider.Clock); Login(session);
                provider.Clock.Now = provider.Clock.Now.AddHours(2);
                Task.WhenAll(session.GetIdTokenAsync(CancellationToken.None), session.GetIdTokenAsync(CancellationToken.None)).GetAwaiter().GetResult();
                Equal(1, provider.RefreshCalls);
            });
            Check("Restaurar sesión permite abrir su espacio sin conexión", () =>
            {
                var provider = new FakeIdentity(); var storage = new MemorySession(); var original = new AccountSession(provider, storage, provider.Clock); Login(original, true);
                var restored = new AccountSession(provider, storage, provider.Clock); restored.RestoreLocalSession();
                Equal("account-one", restored.Current.Uid); Equal(true, restored.NeedsOnlineCheck); Equal(0, provider.RefreshCalls);
                restored.GetIdTokenAsync(CancellationToken.None).GetAwaiter().GetResult(); Equal(1, provider.RefreshCalls);
            });
            Check("Correo sin verificar no obtiene token para sincronizar", () =>
            {
                var provider = new FakeIdentity { Verified = false }; var session = new AccountSession(provider, new MemorySession(), provider.Clock); Login(session);
                IdentityReject("email_unverified", () => session.GetIdTokenAsync(CancellationToken.None));
                provider.Verified = true; session.CheckIdentityAsync(CancellationToken.None).GetAwaiter().GetResult();
                Equal("id-token", session.GetIdTokenAsync(CancellationToken.None).GetAwaiter().GetResult());
            });
            Check("Renovación de otra identidad no cambia de propietario", () =>
            {
                var provider = new FakeIdentity(); var storage = new MemorySession(); var session = new AccountSession(provider, storage, provider.Clock); Login(session, true);
                provider.Uid = "account-two";
                IdentityReject("identity_mismatch", () => session.CheckIdentityAsync(CancellationToken.None));
                Equal("account-one", session.Current.Uid); Equal(null, storage.Saved);
            });
            Check("Revocación descarta el token persistido y cierre conserva obras", () =>
            {
                var provider = new FakeIdentity(); var storage = new MemorySession(); var session = new AccountSession(provider, storage, provider.Clock); Login(session, true);
                provider.RejectRefresh = true; IdentityReject("session_expired", () => session.CheckIdentityAsync(CancellationToken.None)); Equal(null, storage.Saved);
                session.SignOutAsync(CancellationToken.None).GetAwaiter().GetResult(); Equal(null, session.Current);
            });
            Check("DPAPI protege sesión y la vincula al proyecto", () =>
            {
                var file = Path.Combine(root, "identity", "test.session"); var storage = new ProtectedSessionStore(file, "project-one");
                storage.Save(new RememberedSession { Uid = "account-one", Email = "test@example.com", RefreshToken = "secret-refresh-token" });
                Equal(false, Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("secret-refresh-token"));
                Equal("secret-refresh-token", storage.Read().RefreshToken);
                IdentityReject("session_unreadable", () => Task.Run(() => new ProtectedSessionStore(file, "project-two").Read()));
                storage.Clear(); Equal(null, storage.Read());
            });
            Check("Espacios SQLite separados por proyecto y UID sin traversal", () =>
            {
                var a = AccountWorkspace.DatabasePath(root, "project-one", "../account");
                var b = AccountWorkspace.DatabasePath(root, "project-one", "another");
                var c = AccountWorkspace.DatabasePath(root, "project-two", "../account");
                Equal(false, a == b || a == c); Equal(true, a.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar));
                Equal(false, a.Contains(".."));
                var first = new Trazal.Data.LocalStore(a); var second = new Trazal.Data.LocalStore(b);
                first.SaveWork(new Trazal.Core.Work { Title = "Solo cuenta A", Type = Trazal.Core.Work.Types[0] });
                Equal(1, first.GetWorks().Count); Equal(0, second.GetWorks().Count);
            });
            Check("Formulario exige configuración y confirmación coincidente", () =>
            {
                var vm = new AccountViewModel(null, false, "Pendiente", () => { }, () => { });
                vm.Email = "test@example.com"; vm.Password = "test-password"; Equal(false, vm.Submit.CanExecute(null));
                var provider = new FakeIdentity(); vm = new AccountViewModel(new AccountSession(provider, new MemorySession(), provider.Clock), true, "", () => { }, () => { });
                vm.RegisterMode.Execute(null); vm.Email = "test@example.com"; vm.Password = "test-password"; vm.Confirmation = "otro-password";
                Equal(false, vm.Submit.CanExecute(null)); vm.Confirmation = "test-password"; Equal(true, vm.Submit.CanExecute(null));
            });
            Check("Adaptador Firebase envía login y comprueba identidad vía lookup", () =>
            {
                var handler = new FirebaseHttp();
                handler.Replies.Enqueue("{\"localId\":\"user-one\",\"idToken\":\"id-token\",\"refreshToken\":\"refresh-token\",\"expiresIn\":\"3600\"}");
                handler.Replies.Enqueue("{\"users\":[{\"localId\":\"user-one\",\"email\":\"a@example.com\",\"emailVerified\":true}]}");
                using (var provider = new FirebaseIdentityProvider("test-api-key", handler))
                {
                    var result = provider.SignInAsync("a@example.com", "secret-password", CancellationToken.None).GetAwaiter().GetResult();
                    Equal("user-one", result.Identity.Uid); Equal(true, result.Identity.EmailVerified);
                    Equal(true, handler.Paths[0].Contains("accounts:signInWithPassword")); Equal(true, handler.Paths[1].Contains("accounts:lookup"));
                    Equal(false, handler.Paths[0].Contains("secret-password")); Equal(true, handler.Bodies[0].Contains("returnSecureToken"));
                }
            });
            Check("Recuperación no revela si el correo existe", () =>
            {
                var handler = new FirebaseHttp { Status = HttpStatusCode.BadRequest }; handler.Replies.Enqueue("{\"error\":{\"message\":\"EMAIL_NOT_FOUND\"}}");
                using (var provider = new FirebaseIdentityProvider("test-api-key", handler)) provider.SendPasswordResetAsync("a@example.com", CancellationToken.None).GetAwaiter().GetResult();
            });
            Check("Registro y verificación usan los endpoints Firebase correctos", () =>
            {
                var handler = new FirebaseHttp();
                handler.Replies.Enqueue("{\"localId\":\"user-one\",\"idToken\":\"id-token\",\"refreshToken\":\"refresh-token\",\"expiresIn\":\"3600\"}");
                handler.Replies.Enqueue("{\"users\":[{\"localId\":\"user-one\",\"email\":\"a@example.com\",\"emailVerified\":false}]}");
                handler.Replies.Enqueue("{}");
                using (var provider = new FirebaseIdentityProvider("test-api-key", handler))
                {
                    var result = provider.RegisterAsync("a@example.com", "test-password", CancellationToken.None).GetAwaiter().GetResult();
                    Equal(false, result.Identity.EmailVerified);
                    provider.SendVerificationAsync(result.IdToken, CancellationToken.None).GetAwaiter().GetResult();
                    Equal(true, handler.Paths[0].Contains("accounts:signUp")); Equal(true, handler.Bodies[2].Contains("VERIFY_EMAIL"));
                }
            });
            Check("Renovación usa formulario y rota el refresh token", () =>
            {
                var handler = new FirebaseHttp();
                handler.Replies.Enqueue("{\"user_id\":\"user-one\",\"id_token\":\"new-id-token\",\"refresh_token\":\"rotated-refresh\",\"expires_in\":\"3600\"}");
                handler.Replies.Enqueue("{\"users\":[{\"localId\":\"user-one\",\"email\":\"a@example.com\",\"emailVerified\":true}]}");
                using (var provider = new FirebaseIdentityProvider("test-api-key", handler))
                {
                    var result = provider.RefreshAsync("old+refresh", CancellationToken.None).GetAwaiter().GetResult();
                    Equal("rotated-refresh", result.RefreshToken); Equal("new-id-token", result.IdToken);
                    Equal(true, handler.Paths[0].StartsWith("https://securetoken.googleapis.com/v1/token"));
                    Equal(true, handler.Bodies[0].Contains("grant_type=refresh_token")); Equal(true, handler.Bodies[0].Contains("old%2Brefresh"));
                }
            });
            Check("Error Firebase no expone respuesta ni credenciales", () =>
            {
                var handler = new FirebaseHttp { Status = HttpStatusCode.BadRequest }; handler.Replies.Enqueue("{\"error\":{\"message\":\"UNKNOWN secret-value\"}}");
                using (var provider = new FirebaseIdentityProvider("test-api-key", handler))
                {
                    try { provider.SignInAsync("a@example.com", "secret-password", CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Error aceptado"); }
                    catch (IdentityException ex) { Equal(false, ex.Message.Contains("secret")); }
                }
            });
        }
        private static void PreviewIdentityQa(string folder)
        {
            var store = ReportFixture(folder);
            var app = new System.Windows.Application();
            foreach (var name in new[] { "Styles.xaml", "DateControls.xaml" })
                app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("/Trazal;component/" + name, UriKind.Relative) });
            var shell = new Trazal.Desktop.ShellViewModel(store); Trazal.Desktop.Theme.Apply("Claro"); shell.ShowAccount.Execute(null);
            app.Run(new Trazal.Desktop.MainWindow { DataContext = shell, Title = "Trazal · Revisión de acceso", Width = 1100, Height = 820, ShowActivated = false });
        }
    }
}
