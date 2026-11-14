using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Trazal.Core;
using Trazal.Data;
using Trazal.Desktop;

namespace Trazal.Tests
{
    internal static partial class Program
    {
        private sealed class FakeCloud : ISyncRemote
        {
            public string Origin=>"https://sync.test";public string User="test-user";public bool DropAfterWrite;
            public int Writes;private long clock;
            public readonly Dictionary<string,CloudWork> Works=new Dictionary<string,CloudWork>();
            private readonly Dictionary<string,string> receipts=new Dictionary<string,string>();
            public Task<string> UserAsync(CancellationToken token)=>Task.FromResult(User);
            public Task<string> PutAsync(SyncJob job,CancellationToken token)
            {
                string prior;if(receipts.TryGetValue(job.RequestId,out prior))return Task.FromResult(prior);
                CloudWork current;Works.TryGetValue(job.WorkId,out current);
                if((current?.Revision??"0")!=job.Revision)throw new CloudException(409,"revision_conflict","Otra computadora modificó la obra.");
                var revision=(++clock).ToString();Works[job.WorkId]=new CloudWork{Revision=revision,Document=SyncJson.Read<SyncDocument>(job.Json)};receipts[job.RequestId]=revision;Writes++;
                if(DropAfterWrite){DropAfterWrite=false;throw new IOException("Se perdió la respuesta después del envío.");}return Task.FromResult(revision);
            }
            public Task<CloudChanges> ChangesAsync(string cursor,CancellationToken token)
            {
                var rows=Works.Where(p=>long.Parse(p.Value.Revision)>long.Parse(cursor)).OrderBy(p=>long.Parse(p.Value.Revision)).Select(p=>new CloudChange{Id=p.Key,Revision=p.Value.Revision}).ToList();
                return Task.FromResult(new CloudChanges{Changes=rows,NextCursor=rows.LastOrDefault()?.Revision??cursor,HasMore=false});
            }
            public Task<CloudWork> GetAsync(string id,CancellationToken token)=>Task.FromResult(Works[id]);
        }
        private static void SyncTests(string folder)
        {
            var db=ReportFixture(Path.Combine(folder,"cloud-source"));var w=db.GetWorks().Single();
            Check("Contrato de sincronización conserva decimales, fechas y el programa",()=>{var snapshot=db.CaptureSyncDocument(w.Id);snapshot.Validate();var restored=SyncJson.Read<SyncDocument>(snapshot.ToJson());Equal(snapshot.ToJson(),restored.ToJson());Equal("100",restored.Concepts.Single().Quantity);Equal(true,restored.Frozen);});
            Check("Cuenta vinculada no puede cambiar de usuario ni servidor",()=>{db.BindSyncAccount("test-user","https://sync.test");bool failed=false;try{db.BindSyncAccount("other","https://sync.test");}catch(InvalidOperationException){failed=true;}Equal(true,failed);failed=false;try{db.BindSyncAccount("test-user","https://other.test");}catch(InvalidOperationException){failed=true;}Equal(true,failed);});
            Check("Cola durable conserva solicitud y documento tras reiniciar",()=>{var job=db.QueueSyncWork(w.Id);var again=new LocalStore(db.FilePath).QueueSyncWork(w.Id);Equal(job.RequestId,again.RequestId);Equal(job.Json,again.Json);});
            Check("Edición durante envío queda pendiente después de confirmar",()=>{var job=db.QueueSyncWork(w.Id);w.Title="Editada mientras se enviaba";db.SaveWork(w);db.AcknowledgeSync(job,"1");var next=db.QueueSyncWork(w.Id);Equal(false,job.RequestId==next.RequestId);Equal("1",next.Revision);Equal(w.Title,SyncJson.Read<SyncDocument>(next.Json).Work.Title);db.DiscardRejectedJob(next);});
            var a=ReportFixture(Path.Combine(folder,"device-a"));var aw=a.GetWorks().Single();var b=new LocalStore(Path.Combine(folder,"device-b.db"));var cloud=new FakeCloud();
            Check("Dos SQLite comparten obra mediante subida y descarga completas",()=>{
                Equal(1,new SyncEngine(a,cloud).RunAsync(CancellationToken.None).GetAwaiter().GetResult().Uploaded);
                Equal(1,new SyncEngine(b,cloud).RunAsync(CancellationToken.None).GetAwaiter().GetResult().Downloaded);
                Equal(a.CaptureSyncDocument(aw.Id).ToJson(),b.CaptureSyncDocument(aw.Id).ToJson());Equal(0,new SyncEngine(a,cloud).RunAsync(CancellationToken.None).GetAwaiter().GetResult().Uploaded);
            });
            Check("Ediciones en dos equipos generan conflicto sin sobrescribir",()=>{
                var bw=b.GetWorks().Single();bw.Title="Cambio desde B";b.SaveWork(bw);new SyncEngine(b,cloud).RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                aw.Title="Cambio local A";a.SaveWork(aw);var result=new SyncEngine(a,cloud).RunAsync(CancellationToken.None).GetAwaiter().GetResult();Equal(1,result.Conflicts);Equal("Cambio local A",a.GetWorks().Single().Title);Equal("Cambio desde B",cloud.Works[aw.Id].Document.Work.Title);Equal((SyncJob)null,a.QueueSyncWork(aw.Id));
            });
            Check("Respuesta perdida se reintenta sin duplicar revisión ni registros",()=>{
                var c=ReportFixture(Path.Combine(folder,"retry"));var remote=new FakeCloud{DropAfterWrite=true};bool failed=false;
                try{new SyncEngine(c,remote).RunAsync(CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){failed=true;}Equal(true,failed);
                var cw=c.GetWorks().Single();var job=c.QueueSyncWork(cw.Id);var reopened=new LocalStore(c.FilePath);Equal(job.RequestId,reopened.QueueSyncWork(cw.Id).RequestId);
                new SyncEngine(reopened,remote).RunAsync(CancellationToken.None).GetAwaiter().GetResult();Equal(1,remote.Writes);Equal((SyncJob)null,reopened.QueueSyncWork(cw.Id));
            });
            Check("Descarga no pisa una edición ocurrida durante la petición",()=>{
                var local=b.CaptureSyncDocument(aw.Id);var hash=SyncJson.Hash(local.ToJson());var work=b.GetWorks().Single();work.Title="Edición mientras descarga";b.SaveWork(work);
                Equal(false,b.ApplySyncDocument(local,"3",hash));Equal(work.Title,b.GetWorks().Single().Title);
            });
            Check("Descarga inválida revierte todo y conserva la obra",()=>{var doc=b.CaptureSyncDocument(aw.Id);var before=doc.ToJson();doc.Progress[0].Quantity="999999";Reject(()=>b.ApplySyncDocument(doc,"99",SyncJson.Hash(before)));Equal(before,b.CaptureSyncDocument(aw.Id).ToJson());});
            Check("Restaurar respaldo elimina cola, cursor y vinculación anteriores",()=>{var path=Path.Combine(folder,"cloud.trazal");db.Backup(path);using(var prepared=db.PrepareRestore(path))db.Restore(prepared);Equal(0,db.GetSyncStates().Count);Equal("0",db.SyncCursor);db.BindSyncAccount("restored-account","https://sync.test");Equal(true,db.QueueSyncWork(w.Id)!=null);});
            Check("Migración v3 a v4 conserva obras y crea respaldo previo",()=>{var file=Path.Combine(folder,"v3.db");var old=new LocalStore(file);old.SaveWork(new Work{Title="Anterior",Type=Work.Types[0]});Sql(file,"DROP TABLE cloud_outbox;DROP TABLE cloud_state;DROP TABLE cloud_identity;PRAGMA user_version=3;");var migrated=new LocalStore(file);Equal("Anterior",migrated.GetWorks().Single().Title);Equal(1,Directory.GetFiles(folder,"v3.db.v3-*.trazal").Length);Equal(0,migrated.GetSyncStates().Count);});
            Check("Vista de sincronización marca navegación y no simula sesión",()=>{var shell=new ShellViewModel(db);shell.ShowSync.Execute(null);Equal(true,shell.IsSyncSection);Equal(false,shell.IsWorksSection);Equal(false,((SyncViewModel)shell.Page).Run.CanExecute(null));});
            Check("Transporte rechaza HTTP público y credenciales en URL",()=>{Reject(()=>new CloudClient("http://server.test",_=>Task.FromResult("token")));Reject(()=>new CloudClient("https://user:password@server.test",_=>Task.FromResult("token")));});
        }
        private static int HttpSyncQa(string folder,string origin)
        {
            try
            {
                var a=ReportFixture(Path.Combine(folder,"http-a"));var b=new LocalStore(Path.Combine(folder,"http-b.db"));
                using(var remote=new CloudClient(origin,_=>Task.FromResult("integration-test"),true))
                {
                    var first=new SyncEngine(a,remote).RunAsync(CancellationToken.None).GetAwaiter().GetResult();Equal(1,first.Uploaded);
                    var second=new SyncEngine(b,remote).RunAsync(CancellationToken.None).GetAwaiter().GetResult();Equal(1,second.Downloaded);
                    var wa=a.GetWorks().Single();Equal(a.CaptureSyncDocument(wa.Id).ToJson(),b.CaptureSyncDocument(wa.Id).ToJson());
                    var wb=b.GetWorks().Single();wb.Title="Edición HTTP desde B";b.SaveWork(wb);Equal(1,new SyncEngine(b,remote).RunAsync(CancellationToken.None).GetAwaiter().GetResult().Uploaded);
                    Equal(1,new SyncEngine(a,remote).RunAsync(CancellationToken.None).GetAwaiter().GetResult().Downloaded);Equal(wb.Title,a.GetWorks().Single().Title);
                    wa=a.GetWorks().Single();wa.Title="Conflicto local A";a.SaveWork(wa);wb.Title="Cambio ganador B";b.SaveWork(wb);new SyncEngine(b,remote).RunAsync(CancellationToken.None).GetAwaiter().GetResult();Equal(1,new SyncEngine(a,remote).RunAsync(CancellationToken.None).GetAwaiter().GetResult().Conflicts);Equal(wa.Title,a.GetWorks().Single().Title);
                }
                Console.WriteLine("OK HTTP: SQLite A -> Worker handler -> PostgreSQL -> SQLite B; actualización y conflicto verificados.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
        private static void PreviewSyncQa(string folder)
        {
            var store=ReportFixture(folder);
            var app=new System.Windows.Application();
            foreach(var name in new[]{"Styles.xaml","DateControls.xaml"})app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary{Source=new Uri("/Trazal;component/"+name,UriKind.Relative)});
            var shell=new ShellViewModel(store);Theme.Apply("Claro");shell.ShowSync.Execute(null);
            app.Run(new MainWindow{DataContext=shell,Title="Trazal · Revisión de sincronización",Width=1100,Height=760,ShowActivated=false});
        }
    }
}
