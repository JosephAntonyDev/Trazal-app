using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Trazal.Identity.Application;
using Trazal.Identity.Domain;

namespace Trazal.Identity.Adapters.Google
{
    // System browser + PKCE; binds only loopback, with no HTTP.sys URL reservation or administrator rights.
    public sealed class GoogleDesktopAuthorization : IGoogleAuthorization, IDisposable
    {
        private readonly string clientId, clientSecret;
        private readonly Action<string> openBrowser;
        private readonly HttpClient http;
        public GoogleDesktopAuthorization(string clientId, string clientSecret, Action<string> openBrowser = null, HttpMessageHandler handler = null)
        {
            this.clientId = clientId; this.clientSecret = clientSecret;
            this.openBrowser = openBrowser ?? (url => { using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { } });
            http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = 65536 };
        }

        public async Task<string> AuthorizeAsync(CancellationToken cancellation)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                try
                {
                    listener.Start(4);
                    using (timeout.Token.Register(listener.Stop))
                    {
                        var redirect = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/oauth/callback";
                        var state = RandomValue(); var verifier = RandomValue();
                        string challenge;
                        using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
                        openBrowser("https://accounts.google.com/o/oauth2/v2/auth?" + Encode(new Dictionary<string, string>
                        {
                            { "client_id", clientId }, { "redirect_uri", redirect }, { "response_type", "code" },
                            { "scope", "openid email" }, { "state", state }, { "code_challenge", challenge },
                            { "code_challenge_method", "S256" }, { "prompt", "select_account" }
                        }));
                        var code = await ReceiveCode(listener, redirect, state, timeout.Token).ConfigureAwait(false);
                        using (var body = new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            { "client_id", clientId }, { "client_secret", clientSecret }, { "code", code },
                            { "redirect_uri", redirect }, { "grant_type", "authorization_code" }, { "code_verifier", verifier }
                        }))
                        using (var response = await http.PostAsync("https://oauth2.googleapis.com/token", body, timeout.Token).ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode) throw new IdentityException("google_exchange", "No se pudo completar el acceso con Google. Revisa el cliente OAuth de escritorio e intenta de nuevo.");
                            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            {
                                var result = (TokenResponse)new DataContractJsonSerializer(typeof(TokenResponse)).ReadObject(stream);
                                if (string.IsNullOrWhiteSpace(result.IdToken)) throw new IdentityException("invalid_response", "Google no devolvió una credencial válida.");
                                return result.IdToken; // Firebase independently validates this credential; never persist Google tokens.
                            }
                        }
                    }
                }
                catch (Exception) when (cancellation.IsCancellationRequested) { throw new OperationCanceledException(cancellation); }
                catch (Exception) when (timeout.IsCancellationRequested) { throw new IdentityException("google_timeout", "Se agotó el tiempo para entrar con Google. Intenta de nuevo."); }
                catch (HttpRequestException) { throw new IdentityException("network", "No se pudo conectar con Google. Revisa tu conexión."); }
                catch (OperationCanceledException) { throw new IdentityException("network", "Google tardó demasiado en responder. Intenta de nuevo."); }
                catch (SerializationException) { throw new IdentityException("invalid_response", "No se pudo comprobar la respuesta de Google."); }
                finally { listener.Stop(); }
            }
        }

        private static async Task<string> ReceiveCode(TcpListener listener, string redirect, string expectedState, CancellationToken cancellation)
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                using (var connection = await listener.AcceptTcpClientAsync().ConfigureAwait(false))
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                using (var stream = connection.GetStream())
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    using (timeout.Token.Register(connection.Close))
                    {
                        try
                        {
                            var header = new StringBuilder(); var buffer = new byte[1];
                            while (header.Length < 8192 && !header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                            {
                                if (await stream.ReadAsync(buffer, 0, 1, timeout.Token).ConfigureAwait(false) == 0) break;
                                header.Append((char)buffer[0]);
                            }
                            var firstLine = header.ToString().Split('\r')[0].Split(' ');
                            var target = firstLine.Length == 3 && firstLine[0] == "GET" ? firstLine[1] : "";
                            string code = null, error = null;
                            bool valid = false;
                            var completeHeader = header.ToString();
                            var correctHost = false;
                            foreach (var line in completeHeader.Split(new[] { "\r\n" }, StringSplitOptions.None))
                                if (line.Equals("Host: " + new Uri(redirect).Authority, StringComparison.OrdinalIgnoreCase)) correctHost = true;
                            if (correctHost && completeHeader.EndsWith("\r\n\r\n", StringComparison.Ordinal) && target.StartsWith("/oauth/callback?", StringComparison.Ordinal) && header.Length < 8192)
                            {
                                var values = ParseQuery(target.Substring(target.IndexOf('?') + 1));
                                string state;
                                valid = values.TryGetValue("state", out state) && state == expectedState;
                                values.TryGetValue("code", out code); values.TryGetValue("error", out error);
                                valid = valid && ((code != null && code.Length > 0 && error == null) || (error != null && code == null));
                            }
                            var html = "<!doctype html><html lang='es'><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title>Trazal</title><body><h1>Trazal</h1><p>" +
                                (valid ? "Puedes volver a Trazal para ver el resultado y cerrar esta pestaña." : "Solicitud no válida. Continúa en la pestaña de Google o vuelve a Trazal.") + "</p></body></html>";
                            var bytes = Encoding.UTF8.GetBytes(html);
                            var headers = Encoding.ASCII.GetBytes("HTTP/1.1 " + (valid ? "200 OK" : "400 Bad Request") + "\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; frame-ancestors 'none'\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\nContent-Length: " + bytes.Length + "\r\n\r\n");
                            await stream.WriteAsync(headers, 0, headers.Length, timeout.Token).ConfigureAwait(false);
                            await stream.WriteAsync(bytes, 0, bytes.Length, timeout.Token).ConfigureAwait(false);
                            if (!valid) continue;
                            if (error != null) throw new IdentityException("google_cancelled", "No se autorizó el acceso con Google. Puedes intentarlo de nuevo.");
                            return code;
                        }
                        catch (IOException) { cancellation.ThrowIfCancellationRequested(); }
                        catch (ObjectDisposedException) { cancellation.ThrowIfCancellationRequested(); }
                        catch (OperationCanceledException) { cancellation.ThrowIfCancellationRequested(); }
                        catch (ArgumentException) { /* Invalid/duplicate query: wait for the real callback. */ }
                    }
                }
            }
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in query.Split('&'))
            {
                var pair = part.Split(new[] { '=' }, 2);
                if (pair.Length == 2) result.Add(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1].Replace("+", " ")));
            }
            return result;
        }
        private static string RandomValue() { var bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return Base64Url(bytes); }
        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static string Encode(Dictionary<string, string> fields)
        {
            var items = new List<string>();
            foreach (var field in fields) items.Add(Uri.EscapeDataString(field.Key) + "=" + Uri.EscapeDataString(field.Value));
            return string.Join("&", items);
        }
        public void Dispose() => http.Dispose();
        [DataContract] private sealed class TokenResponse { [DataMember(Name = "id_token")] public string IdToken { get; set; } }
    }
}
