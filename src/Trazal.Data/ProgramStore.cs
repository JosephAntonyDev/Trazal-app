using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Trazal.Core;

namespace Trazal.Data
{
    public sealed partial class LocalStore
    {
        private static void EnsureDraft(SQLiteConnection db, SQLiteTransaction tx, string workId)
        {
            if (Scalar(db,"SELECT work_id FROM baselines WHERE work_id=@p0",tx,workId) != null)
                throw new ArgumentException("El programa está fijado. El catálogo y las cantidades programadas se conservan como línea base.");
        }
        public bool IsFrozen(string workId)
        { using(var db=Open()) return Scalar(db,"SELECT work_id FROM baselines WHERE work_id=@p0",null,workId)!=null; }

        public void ImportConcepts(string workId, IList<Concept> concepts)
        {
            if(concepts.Count==0) throw new ArgumentException("Selecciona al menos un concepto.");
            foreach(var c in concepts) { c.Validate(); if(c.WorkId!=workId) throw new ArgumentException("El concepto pertenece a otra obra."); }
            if(concepts.Select(c=>c.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=concepts.Count)
                throw new ArgumentException("Hay claves repetidas en la selección.");
            using(var db=Open()) using(var tx=db.BeginTransaction())
            {
                EnsureDraft(db,tx,workId);
                foreach(var c in concepts)
                {
                    if(Scalar(db,"SELECT id FROM concepts WHERE work_id=@p0 AND code=@p1",tx,workId,c.Code.Trim())!=null)
                        throw new ArgumentException("La clave " + c.Code + " ya existe. No se importó ninguna fila.");
                    Execute(db,"INSERT INTO concepts(id,work_id,code,description,unit,quantity,price) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",tx,c.Id,workId,c.Code.Trim(),c.Description.Trim(),c.Unit.Trim(),Number(c.Quantity),Number(c.UnitPrice));
                }
                tx.Commit();
            }
        }
        public List<PlannedDay> GetPlan(string workId)
        {
            using(var db=Open()) return ReadPlan(db,null,workId);
        }
        private static List<PlannedDay> ReadPlan(SQLiteConnection db, SQLiteTransaction tx, string workId)
        {
            var items=new List<PlannedDay>();
            using(var cmd=Command(db,"SELECT p.concept_id,p.date,p.quantity FROM planned_days p JOIN concepts c ON c.id=p.concept_id WHERE c.work_id=@p0 ORDER BY p.date",tx,workId))
            using(var r=cmd.ExecuteReader()) while(r.Read()) items.Add(new PlannedDay{ConceptId=r.GetString(0),Date=DateTime.ParseExact(r.GetString(1),"yyyy-MM-dd",Inv),Quantity=decimal.Parse(r.GetString(2),Inv)});
            return items;
        }
        private static List<Concept> ReadCatalog(SQLiteConnection db, SQLiteTransaction tx, string workId)
        {
            var items=new List<Concept>();
            using(var cmd=Command(db,"SELECT id,code,description,unit,quantity,price FROM concepts WHERE work_id=@p0",tx,workId))
            using(var r=cmd.ExecuteReader()) while(r.Read()) items.Add(new Concept{Id=r.GetString(0),WorkId=workId,Code=r.GetString(1),Description=r.GetString(2),Unit=r.GetString(3),Quantity=decimal.Parse(r.GetString(4),Inv),UnitPrice=decimal.Parse(r.GetString(5),Inv)});
            return items;
        }
        public void SavePlan(string workId,string conceptId,IList<PlannedDay> days)
        {
            using(var db=Open()) using(var tx=db.BeginTransaction())
            {
                EnsureDraft(db,tx,workId);
                var c=ReadCatalog(db,tx,workId).SingleOrDefault(x=>x.Id==conceptId);
                if(c==null) throw new ArgumentException("Concepto no disponible en esta obra.");
                Scheduling.ValidateDays(c,days,false);
                Execute(db,"DELETE FROM planned_days WHERE concept_id=@p0",tx,conceptId);
                foreach(var d in days) Execute(db,"INSERT INTO planned_days(concept_id,date,quantity) VALUES(@p0,@p1,@p2)",tx,conceptId,d.Date.ToString("yyyy-MM-dd",Inv),Number(d.Quantity));
                tx.Commit();
            }
        }
        public void FreezeProgram(string workId)
        {
            using(var db=Open()) using(var tx=db.BeginTransaction())
            {
                EnsureDraft(db,tx,workId);
                var concepts=ReadCatalog(db,tx,workId); var days=ReadPlan(db,tx,workId);
                if(concepts.Count==0) throw new ArgumentException("Agrega conceptos al presupuesto antes de fijar el programa.");
                foreach(var c in concepts) Scheduling.ValidateDays(c,days.Where(d=>d.ConceptId==c.Id).ToList(),true);
                Execute(db,"INSERT INTO baselines(work_id,approved) VALUES(@p0,@p1)",tx,workId,DateTime.UtcNow.ToString("o",Inv));
                tx.Commit();
            }
        }
        public List<ProgressEntry> GetProgress(string workId)
        { using(var db=Open()) return ReadProgress(db,null,workId); }
        private static List<ProgressEntry> ReadProgress(SQLiteConnection db, SQLiteTransaction tx, string workId)
        {
            var items=new List<ProgressEntry>();
            using(var cmd=Command(db,"SELECT p.id,p.concept_id,p.date,p.quantity,p.note FROM progress p JOIN concepts c ON c.id=p.concept_id WHERE c.work_id=@p0 ORDER BY p.date DESC",tx,workId))
            using(var r=cmd.ExecuteReader()) while(r.Read()) items.Add(new ProgressEntry{Id=r.GetString(0),ConceptId=r.GetString(1),Date=DateTime.ParseExact(r.GetString(2),"yyyy-MM-dd",Inv),Quantity=decimal.Parse(r.GetString(3),Inv),Note=r.GetString(4)});
            return items;
        }
        public void AddProgress(string workId,ProgressEntry entry)
        {
            using(var db=Open()) using(var tx=db.BeginTransaction())
            {
                if(Scalar(db,"SELECT work_id FROM baselines WHERE work_id=@p0",tx,workId)==null) throw new ArgumentException("Primero fija el programa de obra.");
                var c=ReadCatalog(db,tx,workId).SingleOrDefault(x=>x.Id==entry.ConceptId);
                if(c==null) throw new ArgumentException("El concepto no pertenece a esta obra.");
                Scheduling.ValidateProgress(c,entry,ReadProgress(db,tx,workId).Where(p=>p.ConceptId==entry.ConceptId));
                Execute(db,"INSERT INTO progress(id,concept_id,date,quantity,note) VALUES(@p0,@p1,@p2,@p3,@p4)",tx,entry.Id,entry.ConceptId,entry.Date.ToString("yyyy-MM-dd",Inv),Number(entry.Quantity),(entry.Note??"").Trim());
                tx.Commit();
            }
        }
    }
}
