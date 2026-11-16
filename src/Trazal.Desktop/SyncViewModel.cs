using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Trazal.Data;
using Trazal.Identity.Domain;

namespace Trazal.Desktop
{
    public sealed class SyncRow
    { public string Title { get; set; } public string State { get; set; } public string Detail { get; set; } }

    public sealed class SyncViewModel
    {
        public List<SyncRow> Rows { get; }
        public string Connection { get; }
        public string Guidance { get; }
        public string Result { get; }
        public bool HasResult => !string.IsNullOrEmpty(Result);
        public bool NeedsAccount { get; }
        public bool IsEmpty => Rows.Count == 0;
        public ICommand Run { get; }
        public ICommand CheckConnection { get; }
        public ICommand Account { get; }

        public SyncViewModel(ShellViewModel shell, LocalStore store, bool connected)
        {
            Connection = connected ? "Tu cuenta está lista para sincronizar" : "Tus obras están guardadas en este equipo";
            Guidance = shell.SyncGuidance;
            Result = shell.SyncResultMessage;
            NeedsAccount = !connected;
            Account = shell.ShowAccount;
            var states = store.GetSyncStates().ToDictionary(s => s.WorkId);
            Rows = store.GetWorks().Select(w =>
            {
                SyncState state; states.TryGetValue(w.Id, out state);
                bool conflict = !string.IsNullOrEmpty(state?.Conflict);
                bool copied = state != null && state.Revision != "0";
                return new SyncRow
                {
                    Title = w.Title,
                    State = conflict ? "Requiere revisión" : copied ? "Con copia en la nube" : "Solo en este equipo",
                    Detail = conflict ? state.Conflict : copied ? "Sincroniza para enviar los cambios locales." : "Se enviará al sincronizar."
                };
            }).ToList();
            Run = new ActionCommand(async _ => await shell.SyncNow(), _ => connected && !shell.IsBusy);
            CheckConnection = new ActionCommand(async _ => await shell.CheckSyncConnection(), _ => connected && !shell.IsBusy);
        }
    }

    public sealed partial class ShellViewModel
    {
        private SyncEngine syncEngine;
        private string syncOrigin;
        public string SyncResultMessage { get; private set; } = "";
        public string SyncGuidance => !accountConfigured ? "Falta configurar el acceso a tu cuenta. Puedes seguir trabajando sin conexión."
            : accountSession?.Current == null ? "Inicia sesión en Mi cuenta para conectar tus equipos."
            : accountSession.NeedsOnlineCheck ? "Comprueba tu sesión en Mi cuenta para volver a conectar."
            : !accountSession.Current.EmailVerified ? "Confirma tu correo y comprueba la sesión en Mi cuenta."
            : string.IsNullOrWhiteSpace(syncOrigin) ? "Falta configurar el servidor de sincronización en esta instalación."
            : "Comprueba la conexión o sincroniza cuando quieras. Las obras se envían solo al pulsar Sincronizar.";
        public bool IsSyncSection => Page is SyncViewModel;
        public ICommand ShowSync { get; private set; }
        public void ConfigureSyncAvailability(string origin) { syncOrigin = origin; if (IsSyncSection) OpenSync(); }
        public void ConfigureSync(ISyncRemote remote, string expectedUid = null)
        {
            syncOrigin = remote.Origin;
            syncEngine = new SyncEngine(store, remote, expectedUid);
            if (IsSyncSection) OpenSync();
        }
        private void OpenSync() { Page = new SyncViewModel(this, store, syncEngine != null); Location = "Sincronización"; }

        public Task CheckSyncConnection() => RunSyncOperation(async () =>
        {
            await syncEngine.CheckConnectionAsync(CancellationToken.None);
            return "Conexión comprobada: tu cuenta y la base de datos responden. No se enviaron ni descargaron obras.";
        }, "Comprobando cuenta y conexión…");

        public Task SyncNow() => RunSyncOperation(async () =>
        {
            var result = await syncEngine.RunAsync(CancellationToken.None);
            return result.Uploaded + " enviadas · " + result.Downloaded + " descargadas · " + result.Conflicts + " por revisar"
                + (result.Errors.Count > 0 ? " · " + string.Join(" / ", result.Errors) : "");
        }, "Sincronizando obras…");

        private async Task RunSyncOperation(Func<Task<string>> action, string progress)
        {
            if (syncEngine == null || busy) return;
            busy = true; Status = progress; SyncResultMessage = "";
            try { SyncResultMessage = await Task.Run(action); }
            catch (IdentityException ex) { SyncResultMessage = ex.Message; }
            catch (CloudException ex)
            {
                SyncResultMessage = ex.Status == 429 ? "Hay muchas solicitudes. Espera un minuto antes de volver a intentarlo."
                    : ex.Status == 401 ? "No se pudo comprobar tu cuenta. Vuelve a Mi cuenta y comprueba la sesión."
                    : ex.Status >= 500 ? "El servicio no está disponible por ahora. Tus cambios siguen guardados en este equipo; vuelve a intentarlo en unos momentos."
                    : ex.Message;
            }
            catch (OperationCanceledException) { SyncResultMessage = "La conexión tardó demasiado. Tus cambios siguen guardados; puedes volver a intentarlo."; }
            catch (HttpRequestException) { SyncResultMessage = "No se pudo conectar. Revisa tu conexión a internet y vuelve a intentarlo; tus obras siguen en este equipo."; }
            catch (Exception) { SyncResultMessage = "No se pudo completar la operación. Tus obras siguen guardadas en este equipo. Vuelve a intentarlo o contacta al equipo de soporte."; }
            finally { Status = SyncResultMessage; busy = false; OpenSync(); }
        }
    }
}
