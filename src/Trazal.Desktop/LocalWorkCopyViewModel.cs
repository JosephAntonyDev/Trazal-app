using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Trazal.Core;
using Trazal.Data;

namespace Trazal.Desktop
{
    public sealed class LocalWorkCopyRow : Observable
    {
        public Work Work { get; }
        public string Title => Work.Title;
        public string Detail => Work.Type + " · " + Work.ConceptCount + " conceptos · " + ShellViewModel.Money(Work.Subtotal) + " sin IVA";
        public bool Available { get; }
        private bool copied;
        public void MarkCopied() { copied = true; Notify(nameof(State)); }
        public string State => copied ? "Disponible en esta cuenta" : Available ? "Lista para copiar" : "Ya existe en esta cuenta; se conserva sin cambios";
        private bool include;
        public bool Include { get => include; set { if (Available) Set(ref include, value); } }
        public LocalWorkCopyRow(Work work, bool available) { Work = work; Available = available; }
    }
    public sealed class LocalWorkCopyViewModel : Observable, IDisposable
    {
        private readonly PreparedLocalWorkCopy prepared;
        private readonly LocalStore target;
        private readonly Func<bool> identityIsCurrent;
        private readonly Action<bool> setBusy;
        private readonly string uid;
        private bool confirmed, working, completed;
        private string result = "", backupPath = "";
        public List<LocalWorkCopyRow> Rows { get; }
        public string Destination { get; }
        public bool Empty => Rows.Count == 0;
        public bool CanEdit => !working && !completed;
        public bool Confirmed { get => confirmed; set { if (Set(ref confirmed, value)) CommandManager.InvalidateRequerySuggested(); } }
        public string Summary => Rows.Count(r => r.Include) + " seleccionadas · " + Rows.Count(r => !r.Available) + " ya presentes";
        public string Result { get => result; private set { Set(ref result, value); Notify(nameof(HasResult)); } }
        public bool HasResult => Result.Length > 0;
        public string BackupPath { get => backupPath; private set { Set(ref backupPath, value); Notify(nameof(HasBackup)); } }
        public bool HasBackup => BackupPath.Length > 0;
        public ICommand Copy { get; }
        public ICommand SelectAll { get; }
        public ICommand Clear { get; }
        public ICommand Back { get; }
        public LocalWorkCopyViewModel(PreparedLocalWorkCopy prepared, LocalStore target, string uid, string email, Func<bool> identityIsCurrent, Action<bool> setBusy, Action back)
        {
            this.prepared = prepared; this.target = target; this.uid = uid; this.identityIsCurrent = identityIsCurrent; this.setBusy = setBusy;
            Destination = "Destino: " + email;
            var existing = new HashSet<string>(target.GetWorks().Select(w => w.Id));
            Rows = prepared.Works.Select(w => new LocalWorkCopyRow(w, !existing.Contains(w.Id))).ToList();
            foreach (var row in Rows) row.PropertyChanged += (_, __) => { Notify(nameof(Summary)); CommandManager.InvalidateRequerySuggested(); };
            Copy = new ActionCommand(async _ => await CopyAsync(), _ => CanCopy());
            SelectAll = new ActionCommand(_ => { foreach (var row in Rows) row.Include = true; }, _ => CanEdit && Rows.Any(r => r.Available));
            Clear = new ActionCommand(_ => { foreach (var row in Rows) row.Include = false; }, _ => CanEdit && Rows.Any(r => r.Include));
            Back = new ActionCommand(_ => back(), _ => !working);
        }
        private bool CanCopy() => CanEdit && Confirmed && Rows.Any(r => r.Include) && identityIsCurrent();
        public async Task CopyAsync()
        {
            if (!CanCopy()) return;
            var selected = Rows.Where(r => r.Include).Select(r => r.Work.Id).ToArray();
            working = true; setBusy(true); Notify(nameof(CanEdit)); CommandManager.InvalidateRequerySuggested();
            Result = "Preparando respaldo y copiando las obras seleccionadas…";
            try
            {
                var copied = await Task.Run(() => target.CopyLocalWorks(prepared, selected, uid));
                BackupPath = copied.BackupPath;
                completed = true;
                foreach (var row in Rows.Where(r => r.Include)) row.MarkCopied();
                Result = copied.Copied + " obras copiadas · " + copied.Skipped + " ya presentes. Las originales siguen en tu espacio local. Puedes abrir tus obras ahora; para enviarlas a la nube, usa Sincronización.";
            }
            catch (Exception) { Result = "No se pudo completar la copia. No se agregaron obras parcialmente y los originales siguen intactos. Puedes volver a intentarlo."; }
            finally { working = false; setBusy(false); Notify(nameof(CanEdit)); CommandManager.InvalidateRequerySuggested(); }
        }
        public void Dispose() { prepared.Dispose(); }
    }
    public sealed partial class ShellViewModel
    {
        private string localWorksPath;
        public ICommand CopyLocalWorks { get; private set; }
        public bool CanOfferLocalCopy => accountSession?.Current != null && !string.IsNullOrEmpty(localWorksPath);
        private bool LocalCopyIdentityReady => CanOfferLocalCopy && accountSession.Current.EmailVerified && !accountSession.NeedsOnlineCheck;
        public void ConfigureLocalWorkCopy(string originalPath)
        {
            localWorksPath = originalPath;
            CopyLocalWorks = new ActionCommand(async _ => await OpenLocalWorkCopy(), _ => LocalCopyIdentityReady && !busy && !DialogOpen);
        }
        private async Task OpenLocalWorkCopy()
        {
            if (!LocalCopyIdentityReady || busy || DialogOpen) return;
            if (!File.Exists(localWorksPath)) { Status = "Aún no hay obras en el espacio local original de este usuario de Windows."; return; }
            var uid = accountSession.Current.Uid;
            var email = accountSession.Current.Email;
            busy = true; Status = "Preparando la lista de obras locales…";
            PreparedLocalWorkCopy prepared = null;
            try
            {
                prepared = await Task.Run(() => store.PrepareLocalWorkCopy(localWorksPath));
                if (!LocalCopyIdentityReady || accountSession.Current.Uid != uid) throw new InvalidOperationException();
                Page = new LocalWorkCopyViewModel(prepared, store, uid, email,
                    () => LocalCopyIdentityReady && accountSession.Current.Uid == uid,
                    value => busy = value, OpenHome);
                prepared = null; Location = "Copiar obras locales";
                Status = "Selecciona las obras y confirma tu cuenta de destino. Todavía no se ha copiado ninguna.";
            }
            catch (Exception) { Status = "No se pudo preparar la copia. Tus obras siguen intactas; comprueba tu sesión e inténtalo de nuevo."; }
            finally { prepared?.Dispose(); busy = false; }
        }
        public void Dispose() { (Page as IDisposable)?.Dispose(); }
    }
}
