using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;

namespace Trazal.Data
{
    public sealed class SyncJob
    {public string WorkId{get;set;} public string RequestId{get;set;} public string Revision{get;set;} public string Json{get;set;} public string Hash{get;set;}}
    public sealed class SyncState
    {public string WorkId{get;set;} public string Revision{get;set;} public string Hash{get;set;} public string Conflict{get;set;}}
    public sealed partial class LocalStore
    {
        internal const string SyncSchema=@"CREATE TABLE IF NOT EXISTS cloud_identity(id INTEGER PRIMARY KEY CHECK(id=1),uid TEXT NOT NULL,origin TEXT NOT NULL,cursor TEXT NOT NULL DEFAULT '0');
CREATE TABLE IF NOT EXISTS cloud_state(work_id TEXT PRIMARY KEY REFERENCES works(id),revision TEXT NOT NULL DEFAULT '0',synced_hash TEXT NOT NULL DEFAULT '',conflict TEXT NOT NULL DEFAULT '');
CREATE TABLE IF NOT EXISTS cloud_outbox(work_id TEXT PRIMARY KEY REFERENCES works(id),request_id TEXT NOT NULL,revision TEXT NOT NULL,payload TEXT NOT NULL,hash TEXT NOT NULL);";
        public void BindSyncAccount(string uid,string origin)
        {
            if(string.IsNullOrWhiteSpace(uid)||uid.Length>128)throw new ArgumentException("Cuenta inválida.");
            using(var db=Open())using(var tx=db.BeginTransaction())
            {
                var old=Convert.ToString(Scalar(db,"SELECT uid || char(10) || origin FROM cloud_identity WHERE id=1",tx));
                if(old!=""&&old!=uid+"\n"+origin)throw new InvalidOperationException("Este espacio local está vinculado a otra cuenta o servidor. Usa un espacio separado; no se enviaron obras.");
                Execute(db,"INSERT OR IGNORE INTO cloud_identity(id,uid,origin,cursor) VALUES(1,@p0,@p1,'0')",tx,uid,origin);tx.Commit();
            }
        }
        public List<SyncState> GetSyncStates()
        {
            var list=new List<SyncState>();using(var db=Open())using(var cmd=Command(db,"SELECT work_id,revision,synced_hash,conflict FROM cloud_state"))using(var r=cmd.ExecuteReader())while(r.Read())list.Add(new SyncState{WorkId=r.GetString(0),Revision=r.GetString(1),Hash=r.GetString(2),Conflict=r.GetString(3)});return list;
        }
        public string SyncCursor {get{using(var db=Open())return Convert.ToString(Scalar(db,"SELECT cursor FROM cloud_identity WHERE id=1")) is string s&&s.Length>0?s:"0";}}
        public void SaveSyncCursor(string cursor){using(var db=Open())Execute(db,"UPDATE cloud_identity SET cursor=@p0 WHERE id=1",null,cursor);}
        public SyncDocument CaptureSyncDocument(string workId)
        {using(var db=Open())using(var tx=db.BeginTransaction()){var d=Capture(db,tx,workId);tx.Commit();return d;}}
        private static SyncDocument Capture(SQLiteConnection db,SQLiteTransaction tx,string workId)
        {
            var d=new SyncDocument();using(var cmd=Command(db,"SELECT id,title,type,scope,vat,created FROM works WHERE id=@p0",tx,workId))using(var r=cmd.ExecuteReader())
            {if(!r.Read())return null;d.Work=new SyncWork{Id=r.GetString(0),Title=r.GetString(1),Type=r.GetString(2),Scope=r.GetString(3),Vat=r.IsDBNull(4)?null:r.GetString(4),CreatedAt=r.GetString(5)};}
            d.Frozen=Scalar(db,"SELECT work_id FROM baselines WHERE work_id=@p0",tx,workId)!=null;
            d.Concepts=ReadCatalog(db,tx,workId).Select(c=>new SyncConcept{Id=c.Id,Code=c.Code,Description=c.Description,Unit=c.Unit,Quantity=SyncDocument.Numeric(c.Quantity),UnitPrice=SyncDocument.Numeric(c.UnitPrice)}).ToList();
            d.Plan=ReadPlan(db,tx,workId).Select(p=>new SyncDay{ConceptId=p.ConceptId,Date=p.Date.ToString("yyyy-MM-dd"),Quantity=SyncDocument.Numeric(p.Quantity)}).ToList();
            d.Progress=ReadProgress(db,tx,workId).Select(p=>new SyncProgress{Id=p.Id,ConceptId=p.ConceptId,Date=p.Date.ToString("yyyy-MM-dd"),Quantity=SyncDocument.Numeric(p.Quantity),Note=p.Note}).ToList();
            using(var cmd=Command(db,"SELECT id,date,category,description,amount,vat FROM expenses WHERE work_id=@p0 ORDER BY id",tx,workId))using(var r=cmd.ExecuteReader())while(r.Read())d.Expenses.Add(new SyncExpense{Id=r.GetString(0),Date=r.GetString(1),Category=r.GetString(2),Description=r.GetString(3),Amount=r.GetString(4),Vat=r.GetString(5)});
            return d;
        }
        public SyncJob QueueSyncWork(string workId)
        {
            using(var db=Open())using(var tx=db.BeginTransaction())
            {
                if(Scalar(db,"SELECT uid FROM cloud_identity WHERE id=1",tx)==null)throw new InvalidOperationException("Vincula una cuenta antes de sincronizar.");
                using(var cmd=Command(db,"SELECT request_id,revision,payload,hash FROM cloud_outbox WHERE work_id=@p0",tx,workId))using(var r=cmd.ExecuteReader())if(r.Read())return new SyncJob{WorkId=workId,RequestId=r.GetString(0),Revision=r.GetString(1),Json=r.GetString(2),Hash=r.GetString(3)};
                var document=Capture(db,tx,workId);if(document==null)return null;document.Validate();var json=document.ToJson();
                if(Encoding.UTF8.GetByteCount(json)>SyncDocument.MaxBytes)throw new ArgumentException("La obra excede 512 KiB de sincronización en el piloto. Sigue guardada en este equipo.");
                var hash=SyncJson.Hash(json);string revision="0",old="",conflict="";
                using(var cmd=Command(db,"SELECT revision,synced_hash,conflict FROM cloud_state WHERE work_id=@p0",tx,workId))using(var r=cmd.ExecuteReader())if(r.Read()){revision=r.GetString(0);old=r.GetString(1);conflict=r.GetString(2);}
                if(hash==old||conflict!="")return null;
                var job=new SyncJob{WorkId=workId,RequestId=Guid.NewGuid().ToString("N"),Revision=revision,Json=json,Hash=hash};
                Execute(db,"INSERT OR IGNORE INTO cloud_state(work_id) VALUES(@p0)",tx,workId);
                Execute(db,"INSERT INTO cloud_outbox(work_id,request_id,revision,payload,hash) VALUES(@p0,@p1,@p2,@p3,@p4)",tx,workId,job.RequestId,revision,json,hash);tx.Commit();return job;
            }
        }
        public void AcknowledgeSync(SyncJob job,string revision)
        {
            using(var db=Open())using(var tx=db.BeginTransaction())
            {
                if(Convert.ToString(Scalar(db,"SELECT request_id FROM cloud_outbox WHERE work_id=@p0",tx,job.WorkId))!=job.RequestId)throw new InvalidOperationException("La cola cambió durante la sincronización.");
                Execute(db,"UPDATE cloud_state SET revision=@p1,synced_hash=@p2,conflict='' WHERE work_id=@p0",tx,job.WorkId,revision,job.Hash);
                Execute(db,"DELETE FROM cloud_outbox WHERE work_id=@p0 AND request_id=@p1",tx,job.WorkId,job.RequestId);tx.Commit();
            }
        }
        public void MarkSyncConflict(string workId,string message)
        {using(var db=Open())using(var tx=db.BeginTransaction()){Execute(db,"INSERT OR IGNORE INTO cloud_state(work_id) VALUES(@p0)",tx,workId);Execute(db,"UPDATE cloud_state SET conflict=@p1 WHERE work_id=@p0",tx,workId,message);Execute(db,"DELETE FROM cloud_outbox WHERE work_id=@p0",tx,workId);tx.Commit();}}
        public void DiscardRejectedJob(SyncJob job)
        {using(var db=Open())Execute(db,"DELETE FROM cloud_outbox WHERE work_id=@p0 AND request_id=@p1",null,job.WorkId,job.RequestId);}
        public bool ApplySyncDocument(SyncDocument document,string revision,string expectedLocalHash)
        {
            document.Validate();var w=document.ToWork();
            using(var db=Open())using(var tx=db.BeginTransaction())
            {
                var local=Capture(db,tx,w.Id);var currentHash=local==null?null:SyncJson.Hash(local.ToJson());
                if(currentHash!=expectedLocalHash||Scalar(db,"SELECT work_id FROM cloud_outbox WHERE work_id=@p0",tx,w.Id)!=null)return false;
                Execute(db,"INSERT OR IGNORE INTO works(id,title,type,scope,vat,created) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)",tx,w.Id,w.Title,w.Type,w.Scope,Number(w.Vat),document.Work.CreatedAt);
                Execute(db,"UPDATE works SET title=@p1,type=@p2,scope=@p3,vat=@p4,created=@p5 WHERE id=@p0",tx,w.Id,w.Title,w.Type,w.Scope,Number(w.Vat),document.Work.CreatedAt);
                Execute(db,"DELETE FROM progress WHERE concept_id IN (SELECT id FROM concepts WHERE work_id=@p0);DELETE FROM planned_days WHERE concept_id IN (SELECT id FROM concepts WHERE work_id=@p0);DELETE FROM baselines WHERE work_id=@p0;DELETE FROM concepts WHERE work_id=@p0;DELETE FROM expenses WHERE work_id=@p0;",tx,w.Id);
                foreach(var c in document.ToConcepts())Execute(db,"INSERT INTO concepts(id,work_id,code,description,unit,quantity,price) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",tx,c.Id,w.Id,c.Code,c.Description,c.Unit,Number(c.Quantity),Number(c.UnitPrice));
                foreach(var p in document.ToPlan())Execute(db,"INSERT INTO planned_days(concept_id,date,quantity) VALUES(@p0,@p1,@p2)",tx,p.ConceptId,p.Date.ToString("yyyy-MM-dd"),Number(p.Quantity));
                if(document.Frozen)Execute(db,"INSERT INTO baselines(work_id,approved) VALUES(@p0,@p1)",tx,w.Id,DateTime.UtcNow.ToString("o",Inv));
                foreach(var p in document.ToProgress())Execute(db,"INSERT INTO progress(id,concept_id,date,quantity,note) VALUES(@p0,@p1,@p2,@p3,@p4)",tx,p.Id,p.ConceptId,p.Date.ToString("yyyy-MM-dd"),Number(p.Quantity),p.Note);
                foreach(var e in document.ToExpenses())Execute(db,"INSERT INTO expenses(id,work_id,date,category,description,amount,vat) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",tx,e.Id,w.Id,e.Date.ToString("yyyy-MM-dd"),e.Category,e.Description,Number(e.Amount),Number(e.Vat));
                var saved=Capture(db,tx,w.Id);Execute(db,"INSERT OR REPLACE INTO cloud_state(work_id,revision,synced_hash,conflict) VALUES(@p0,@p1,@p2,'')",tx,w.Id,revision,SyncJson.Hash(saved.ToJson()));tx.Commit();return true;
            }
        }
        internal void ResetSyncAfterRestore()
        {using(var db=Open())Execute(db,"DELETE FROM cloud_outbox;DELETE FROM cloud_state;DELETE FROM cloud_identity;");}
    }
}
