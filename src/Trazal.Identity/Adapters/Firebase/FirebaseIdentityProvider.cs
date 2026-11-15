using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trazal.Identity.Application;
using Trazal.Identity.Domain;

namespace Trazal.Identity.Adapters.Firebase
{
    public sealed class FirebaseIdentityProvider : IIdentityProvider, IGoogleIdentityProvider, IDisposable
    {
        private readonly string apiKey;
        private readonly HttpClient client;
        public FirebaseIdentityProvider(string apiKey, HttpMessageHandler handler = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("Falta configurar Firebase.");
            this.apiKey = apiKey;
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
        }

        public Task<IdentityTokens> SignInAsync(string email, string password, CancellationToken cancellation)
            => PasswordRequest("signInWithPassword", email, password, cancellation);
        public Task<IdentityTokens> RegisterAsync(string email, string password, CancellationToken cancellation)
            => PasswordRequest("signUp", email, password, cancellation);

        public async Task<IdentityTokens> SignInGoogleAsync(string googleIdToken, CancellationToken cancellation)
        {
            if (string.IsNullOrWhiteSpace(googleIdToken)) throw new IdentityException("invalid_response", "Google no devolvió una credencial válida.");
            var result = await Post<TokenResponse>(AccountUrl("signInWithIdp"), Json(new Dictionary<string, object>
            {
                { "postBody", "id_token=" + Uri.EscapeDataString(googleIdToken) + "&providerId=google.com" },
                { "requestUri", "http://localhost" }, { "returnSecureToken", true }
            }), cancellation).ConfigureAwait(false);
            return await Complete(result.IdToken, result.RefreshToken, result.ExpiresIn, result.LocalId, cancellation).ConfigureAwait(false);
        }

        private async Task<IdentityTokens> PasswordRequest(string action, string email, string password, CancellationToken cancellation)
        {
            var result = await Post<TokenResponse>(AccountUrl(action), Json(new Dictionary<string, object>
                { { "email", email }, { "password", password }, { "returnSecureToken", true } }), cancellation).ConfigureAwait(false);
            return await Complete(result.IdToken, result.RefreshToken, result.ExpiresIn, result.LocalId, cancellation).ConfigureAwait(false);
        }

        public async Task<IdentityTokens> RefreshAsync(string refreshToken, CancellationToken cancellation)
        {
            if (string.IsNullOrEmpty(refreshToken)) throw new IdentityException("session_expired", "Vuelve a iniciar sesión para conectar tu cuenta.");
            var result = await Post<RefreshResponse>("https://securetoken.googleapis.com/v1/token?key=" + Uri.EscapeDataString(apiKey),
                new FormUrlEncodedContent(new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "refresh_token", refreshToken } }), cancellation).ConfigureAwait(false);
            return await Complete(result.IdToken, result.RefreshToken, result.ExpiresIn, result.UserId, cancellation).ConfigureAwait(false);
        }

        private async Task<IdentityTokens> Complete(string idToken, string refreshToken, string expires, string uid, CancellationToken cancellation)
        {
            int seconds;
            if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(refreshToken) || !int.TryParse(expires, out seconds) || seconds <= 0 || seconds > 86400)
                throw new IdentityException("invalid_response", "No se pudo comprobar la sesión.");
            var identity = await GetIdentityAsync(idToken, cancellation).ConfigureAwait(false);
            if (identity.Uid != uid) throw new IdentityException("identity_mismatch", "No se pudo comprobar la cuenta.");
            return new IdentityTokens { Identity = identity, IdToken = idToken, RefreshToken = refreshToken, ExpiresAtUtc = DateTime.UtcNow.AddSeconds(seconds) };
        }

        public async Task<AccountIdentity> GetIdentityAsync(string idToken, CancellationToken cancellation)
        {
            var result = await Post<LookupResponse>(AccountUrl("lookup"), Json(new Dictionary<string, object> { { "idToken", idToken } }), cancellation).ConfigureAwait(false);
            if (result.Users == null || result.Users.Length != 1 || string.IsNullOrWhiteSpace(result.Users[0].LocalId))
                throw new IdentityException("invalid_response", "No se pudo comprobar tu cuenta.");
            var user = result.Users[0];
            return new AccountIdentity { Uid = user.LocalId, Email = user.Email, EmailVerified = user.EmailVerified };
        }

        public async Task SendVerificationAsync(string idToken, CancellationToken cancellation)
            => await Post<EmptyResponse>(AccountUrl("sendOobCode"), Json(new Dictionary<string, object> { { "requestType", "VERIFY_EMAIL" }, { "idToken", idToken } }), cancellation).ConfigureAwait(false);

        public async Task SendPasswordResetAsync(string email, CancellationToken cancellation)
        {
            try { await Post<EmptyResponse>(AccountUrl("sendOobCode"), Json(new Dictionary<string, object> { { "requestType", "PASSWORD_RESET" }, { "email", email } }), cancellation).ConfigureAwait(false); }
            catch (IdentityException ex) when (ex.Code == "invalid_credentials") { /* Same result whether the address exists or not. */ }
        }

        private string AccountUrl(string action) => "https://identitytoolkit.googleapis.com/v1/accounts:" + action + "?key=" + Uri.EscapeDataString(apiKey);
        private static StringContent Json(Dictionary<string, object> data)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Dictionary<string, object>), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).WriteObject(stream, data);
                return new StringContent(Encoding.UTF8.GetString(stream.ToArray()), Encoding.UTF8, "application/json");
            }
        }

        private async Task<T> Post<T>(string url, HttpContent content, CancellationToken cancellation)
        {
            try
            {
                using (content)
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(25));
                    using (var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content })
                    {
                        request.Headers.TryAddWithoutValidation("X-Firebase-Locale", "es");
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new MemoryStream())
                        {
                            var buffer = new byte[8192]; int read;
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                            {
                                if (output.Length + read > 131072) throw new IdentityException("invalid_response", "Respuesta de autenticación inválida.");
                                output.Write(buffer, 0, read);
                            }
                            output.Position = 0;
                            if (!response.IsSuccessStatusCode)
                            {
                                string code = "";
                                try { code = ((ErrorResponse)new DataContractJsonSerializer(typeof(ErrorResponse)).ReadObject(output)).Error?.Message ?? ""; }
                                catch (SerializationException) { }
                                throw MapError(code, (int)response.StatusCode);
                            }
                            return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(output);
                        }
                    }
                }
            }
            catch (HttpRequestException) { throw new IdentityException("network", "No se pudo conectar con Firebase. Revisa tu conexión e intenta de nuevo."); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new IdentityException("network", "La conexión tardó demasiado. Intenta de nuevo."); }
            catch (SerializationException) { throw new IdentityException("invalid_response", "No se pudo interpretar la respuesta de autenticación."); }
        }

        private static IdentityException MapError(string raw, int status)
        {
            var code = raw.Split(' ', ':')[0];
            switch (code)
            {
                case "FEDERATED_USER_ID_ALREADY_LINKED": case "NEED_CONFIRMATION": case "ACCOUNT_EXISTS_WITH_DIFFERENT_CREDENTIAL":
                    return new IdentityException("account_link_required", "Ese correo ya tiene otro método de acceso. Inicia sesión con el método original; la vinculación de cuentas requiere revisión.");
                case "INVALID_IDP_RESPONSE": case "INVALID_OAUTH_CLIENT_ID":
                    return new IdentityException("google_configuration", "Revisa el cliente OAuth de escritorio y el proveedor Google en el mismo proyecto Firebase.");
                case "EMAIL_NOT_FOUND": case "INVALID_PASSWORD": case "INVALID_LOGIN_CREDENTIALS":
                    return new IdentityException("invalid_credentials", "Revisa tu correo y contraseña o utiliza la recuperación de acceso.");
                case "EMAIL_EXISTS": return new IdentityException("registration_failed", "No se pudo crear la cuenta con ese correo. Prueba iniciar sesión o recuperar tu contraseña.");
                case "USER_DISABLED": case "USER_NOT_FOUND": case "TOKEN_EXPIRED": case "INVALID_ID_TOKEN": case "INVALID_REFRESH_TOKEN":
                    return new IdentityException("session_expired", "La sesión ya no está disponible. Vuelve a iniciar sesión.");
                case "WEAK_PASSWORD": case "PASSWORD_DOES_NOT_MEET_REQUIREMENTS":
                    return new IdentityException("weak_password", "La contraseña no cumple la política del proyecto. Usa al menos 8 caracteres y revisa los requisitos configurados.");
                case "INVALID_EMAIL": return new IdentityException("invalid_email", "Revisa el formato del correo electrónico.");
                case "TOO_MANY_ATTEMPTS_TRY_LATER": case "RESET_PASSWORD_EXCEED_LIMIT":
                    return new IdentityException("rate_limit", "Se hicieron demasiados intentos. Espera unos minutos.");
                case "OPERATION_NOT_ALLOWED": case "API_KEY_INVALID": case "PROJECT_NOT_FOUND": case "CONFIGURATION_NOT_FOUND":
                    return new IdentityException("configuration", "Revisa la configuración de Firebase y habilita Correo electrónico/contraseña.");
                default: return new IdentityException(status >= 500 ? "network" : "authentication_failed", "No se pudo completar la solicitud. Intenta de nuevo más tarde.");
            }
        }
        public void Dispose() => client.Dispose();

        [DataContract] private sealed class TokenResponse
        {
            [DataMember(Name = "localId")] public string LocalId { get; set; }
            [DataMember(Name = "idToken")] public string IdToken { get; set; }
            [DataMember(Name = "refreshToken")] public string RefreshToken { get; set; }
            [DataMember(Name = "expiresIn")] public string ExpiresIn { get; set; }
        }
        [DataContract] private sealed class RefreshResponse
        {
            [DataMember(Name = "user_id")] public string UserId { get; set; }
            [DataMember(Name = "id_token")] public string IdToken { get; set; }
            [DataMember(Name = "refresh_token")] public string RefreshToken { get; set; }
            [DataMember(Name = "expires_in")] public string ExpiresIn { get; set; }
        }
        [DataContract] private sealed class LookupResponse { [DataMember(Name = "users")] public UserResponse[] Users { get; set; } }
        [DataContract] private sealed class UserResponse
        {
            [DataMember(Name = "localId")] public string LocalId { get; set; }
            [DataMember(Name = "email")] public string Email { get; set; }
            [DataMember(Name = "emailVerified")] public bool EmailVerified { get; set; }
        }
        [DataContract] private sealed class EmptyResponse { }
        [DataContract] private sealed class ErrorResponse { [DataMember(Name = "error")] public ErrorDetail Error { get; set; } }
        [DataContract] private sealed class ErrorDetail { [DataMember(Name = "message")] public string Message { get; set; } }
    }
}
