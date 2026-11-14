using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Trazal.Data;

namespace Trazal.Desktop
{
    public sealed class SyncRow
    {public string Title{get;set;}public string State{get;set;}public string Detail{get;set;}}
    public sealed class SyncViewModel
    {
        public List<SyncRow> Rows{get;}
        public string Connection{get;}
        public ICommand Run{get;}
        public SyncViewModel(ShellViewModel shell,LocalStore store,bool connected)
        {
            Connection=connected?"Cuenta conectada · sincronización manual":"Sincronización pendiente de conectar tu cuenta";
            var states=store.GetSyncStates().ToDictionary(s=>s.WorkId);
            Rows=store.GetWorks().Select(w=>{SyncState state;states.TryGetValue(w.Id,out state);return new SyncRow{Title=w.Title,State=state==null?"Solo en este equipo":state.Conflict!=""?"Requiere revisión":"Con historial de sincronización",Detail=state?.Conflict??""};}).ToList();
            Run=new ActionCommand(async _=>await shell.SyncNow(),_=>connected&&!shell.IsBusy);
        }
    }
    public sealed partial class ShellViewModel
    {
        private SyncEngine syncEngine;
        public bool IsSyncSection=>Page is SyncViewModel;
        public ICommand ShowSync{get;private set;}
        // Called by the Firebase sign-in integration once a verified token provider is available.
        public void ConfigureSync(ISyncRemote remote){syncEngine=new SyncEngine(store,remote);if(IsSyncSection)OpenSync();}
        private void OpenSync(){Page=new SyncViewModel(this,store,syncEngine!=null);Location="Sincronización";}
        public async Task SyncNow()
        {
            if(syncEngine==null||busy)return;busy=true;Status="Sincronizando obras…";
            try{var result=await Task.Run(()=>syncEngine.RunAsync(CancellationToken.None));Status=result.Uploaded+" enviadas · "+result.Downloaded+" descargadas · "+result.Conflicts+" por revisar"+(result.Errors.Count>0?" · "+string.Join(" / ",result.Errors):"");}
            catch(System.Exception ex){Status="Sincronización pendiente: "+ex.Message;}
            finally{busy=false;OpenSync();}
        }
    }
}
