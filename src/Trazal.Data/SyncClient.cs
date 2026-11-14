using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Trazal.Data
{
    [DataContract] public sealed class CloudRevision { [DataMember(Name="revision")] public string Revision{get;set;} }
    [DataContract] public sealed class CloudWork { [DataMember(Name="revision")] public string Revision{get;set;} [DataMember(Name="document")] public SyncDocument Document{get;set;} }
    [DataContract] public sealed class CloudChange { [DataMember(Name="id")] public string Id{get;set;} [DataMember(Name="revision")] public string Revision{get;set;} }
    [DataContract] public sealed class CloudChanges { [DataMember(Name="changes")] public List<CloudChange> Changes{get;set;} [DataMember(Name="nextCursor")] public string NextCursor{get;set;} [DataMember(Name="hasMore")] public bool HasMore{get;set;} }
    [DataContract] public sealed class CloudUser { [DataMember(Name="uid")] public string Uid{get;set;} }
    [DataContract] internal sealed class CloudErrorBody { [DataMember(Name="error")] public CloudError Error{get;set;} }
    [DataContract] internal sealed class CloudError { [DataMember(Name="code")] public string Code{get;set;} [DataMember(Name="message")] public string Message{get;set;} }
    public sealed class CloudException : Exception
    {
        public int Status{get;} public string Code{get;}
        public CloudException(int status,string code,string message):base(message){Status=status;Code=code;}
    }
    public interface ISyncRemote
    {
        string Origin{get;}
        Task<string> UserAsync(CancellationToken cancellation);
        Task<string> PutAsync(SyncJob job,CancellationToken cancellation);
        Task<CloudChanges> ChangesAsync(string cursor,CancellationToken cancellation);
        Task<CloudWork> GetAsync(string id,CancellationToken cancellation);
    }
    public sealed class CloudClient : ISyncRemote,IDisposable
    {
        private readonly HttpClient client;
        private readonly Func<CancellationToken,Task<string>> tokenProvider;
        public string Origin{get;}
        public CloudClient(string origin,Func<CancellationToken,Task<string>> tokenProvider,bool allowLocalHttp=false)
        {
            var uri=new Uri(origin,UriKind.Absolute);
            if((uri.Scheme!="https"&&!(allowLocalHttp&&uri.Scheme=="http"&&uri.IsLoopback))||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!=""||uri.AbsolutePath!="/")throw new ArgumentException("Usa el origen HTTPS del backend, sin ruta ni credenciales.");
            Origin=uri.GetLeftPart(UriPartial.Authority);this.tokenProvider=tokenProvider??throw new ArgumentNullException(nameof(tokenProvider));
            // Default TLS/certificate validation stays enabled; never follow a redirect with a bearer token.
            client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate}){Timeout=TimeSpan.FromSeconds(30)};
        }
        public async Task<string> UserAsync(CancellationToken cancellation)=>(await Send<CloudUser>("v1/me",null,cancellation)).Uid;
        public async Task<string> PutAsync(SyncJob job,CancellationToken cancellation)=>(await Send<CloudRevision>("v1/works/"+job.WorkId,job,cancellation)).Revision;
        public Task<CloudChanges> ChangesAsync(string cursor,CancellationToken cancellation)=>Send<CloudChanges>("v1/sync/changes?cursor="+Uri.EscapeDataString(cursor),null,cancellation);
        public Task<CloudWork> GetAsync(string id,CancellationToken cancellation)=>Send<CloudWork>("v1/works/"+Uri.EscapeDataString(id),null,cancellation);
        private async Task<T> Send<T>(string path,SyncJob job,CancellationToken cancellation)
        {
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using(var request=new HttpRequestMessage(job==null?HttpMethod.Get:HttpMethod.Put,Origin+"/"+path))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));cancellation=timeout.Token;
                var token=await tokenProvider(cancellation).ConfigureAwait(false);if(string.IsNullOrWhiteSpace(token))throw new CloudException(401,"authentication_required","Inicia sesión para sincronizar.");
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                if(job!=null){request.Headers.TryAddWithoutValidation("If-Match","\""+job.Revision+"\"");request.Headers.Add("Idempotency-Key",job.RequestId);request.Content=new StringContent(job.Json,Encoding.UTF8,"application/json");}
                using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false))
                using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream())
                {
                    var buffer=new byte[8192];int count;while((count=await stream.ReadAsync(buffer,0,buffer.Length,cancellation).ConfigureAwait(false))>0){if(output.Length+count>SyncDocument.MaxBytes+16384)throw new IOException("Respuesta remota demasiado grande.");output.Write(buffer,0,count);}
                    var json=Encoding.UTF8.GetString(output.ToArray());
                    if(!response.IsSuccessStatusCode)
                    {
                        CloudError error=null;try{error=SyncJson.Read<CloudErrorBody>(json).Error;}catch(SerializationException){}
                        throw new CloudException((int)response.StatusCode,error?.Code??"http_error",error?.Message??"No se pudo sincronizar. Tus obras siguen guardadas localmente.");
                    }
                    return SyncJson.Read<T>(json);
                }
            }
        }
        public void Dispose()=>client.Dispose();
    }
    public sealed class SyncResult
    {public int Uploaded{get;set;} public int Downloaded{get;set;} public int Conflicts{get;set;} public List<string> Errors{get;}=new List<string>();}
    public sealed class SyncEngine
    {
        private readonly LocalStore store;private readonly ISyncRemote remote;private readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        public SyncEngine(LocalStore store,ISyncRemote remote){this.store=store;this.remote=remote;}
        private static void CheckRevision(string revision){long n;if(!long.TryParse(revision,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out n)||n<0)throw new IOException("Revisión remota inválida.");}
        public async Task<SyncResult> RunAsync(CancellationToken cancellation)
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var uid=await remote.UserAsync(cancellation).ConfigureAwait(false);store.BindSyncAccount(uid,remote.Origin);var result=new SyncResult();
                foreach(var work in store.GetWorks())
                {
                    cancellation.ThrowIfCancellationRequested();SyncJob job;
                    try{job=store.QueueSyncWork(work.Id);}catch(ArgumentException ex){result.Errors.Add(work.Title+": "+ex.Message);continue;}
                    if(job==null)continue;
                    try{var revision=await remote.PutAsync(job,cancellation).ConfigureAwait(false);CheckRevision(revision);store.AcknowledgeSync(job,revision);result.Uploaded++;}
                    catch(CloudException ex) when(ex.Status==409){store.MarkSyncConflict(work.Id,ex.Message);}
                    catch(CloudException ex) when(ex.Status==400||ex.Status==413||ex.Status==422){store.DiscardRejectedJob(job);result.Errors.Add(work.Title+": "+ex.Message);}
                }
                for(int page=0;page<100;page++)
                {
                    var cursor=store.SyncCursor;var feed=await remote.ChangesAsync(cursor,cancellation).ConfigureAwait(false);
                    if(feed.Changes==null||feed.Changes.Count>100)throw new IOException("Lista de cambios remota inválida.");CheckRevision(feed.NextCursor);
                    long last=long.Parse(cursor);foreach(var change in feed.Changes){CheckRevision(change.Revision);if(long.Parse(change.Revision)<=last)throw new IOException("Orden de cambios inválido.");last=long.Parse(change.Revision);}
                    if(feed.NextCursor!=last.ToString(System.Globalization.CultureInfo.InvariantCulture)||(feed.HasMore&&feed.Changes.Count==0))throw new IOException("Cursor remoto inválido.");
                    foreach(var change in feed.Changes)
                    {
                        var state=store.GetSyncStates().SingleOrDefault(s=>s.WorkId==change.Id);
                        if(state!=null&&(state.Conflict!=""||state.Revision==change.Revision))continue;
                        var local=store.CaptureSyncDocument(change.Id);var hash=local==null?null:SyncJson.Hash(local.ToJson());
                        if(local!=null&&(state==null||hash!=state.Hash)){store.MarkSyncConflict(change.Id,"La obra tiene cambios locales y remotos. Se conservaron ambas versiones.");continue;}
                        var incoming=await remote.GetAsync(change.Id,cancellation).ConfigureAwait(false);CheckRevision(incoming.Revision);
                        if(incoming.Document?.Work?.Id!=change.Id||long.Parse(incoming.Revision)<long.Parse(change.Revision))throw new IOException("La obra descargada no corresponde a la revisión solicitada.");
                        if(store.ApplySyncDocument(incoming.Document,incoming.Revision,hash))result.Downloaded++;
                        else store.MarkSyncConflict(change.Id,"La obra cambió mientras se descargaba. Tus cambios locales se conservaron.");
                    }
                    store.SaveSyncCursor(feed.NextCursor);if(!feed.HasMore)break;
                    if(page==99)throw new IOException("Hay más cambios pendientes. Continúa en la próxima sincronización.");
                }
                result.Conflicts=store.GetSyncStates().Count(s=>s.Conflict!="");return result;
            }
            finally{gate.Release();}
        }
    }
}
