using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using Trazal.Core;

namespace Trazal.Data
{
    public sealed class PreparedRestore : IDisposable
    {
        internal string Folder { get; }
        internal string FilePath => Path.Combine(Folder, "restore.db");
        public string SourceName { get; internal set; }
        public int WorkCount { get; internal set; }
        public int ConceptCount { get; internal set; }
        public int ExpenseCount { get; internal set; }
        public int ProgressCount { get; internal set; }
        public string Owner { get; internal set; }
        public string Summary => WorkCount + " obras · " + ConceptCount + " conceptos · " + ExpenseCount + " gastos · " + ProgressCount + " avances";
        internal PreparedRestore(string folder) { Folder = folder; }
        public void Dispose() { if (Directory.Exists(Folder)) Directory.Delete(Folder, true); }
    }
    public sealed partial class LocalStore
    {
        public PreparedRestore PrepareRestore(string source)
        {
            source = Path.GetFullPath(source);
            if (string.Equals(source, FilePath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Selecciona un respaldo distinto de la base activa.");
            var result = new PreparedRestore(Path.Combine(Path.GetDirectoryName(FilePath), "restore-" + Guid.NewGuid().ToString("N"))) { SourceName = Path.GetFileName(source) };
            Directory.CreateDirectory(result.Folder);
            try
            {
                // Open read-only: never migrate or alter the chosen backup.
                using (var db = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = source, ReadOnly = true, FailIfMissing = true, Pooling = false }.ToString()))
                {
                    db.Open(); CheckBackupSchema(db);
                    using (var copy = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = result.FilePath, Pooling = false }.ToString()))
                    { copy.Open(); db.BackupDatabase(copy, "main", "main", -1, null, 0); }
                }
                // This isolated copy can migrate directly: the selected source is already preserved untouched.
                var staged = new LocalStore(result.FilePath, false);
                staged.ValidateRestoredData(result);
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        private static void CheckBackupSchema(SQLiteConnection db)
        {
            if (Convert.ToString(Scalar(db, "PRAGMA integrity_check")) != "ok") throw new ArgumentException("El respaldo está dañado.");
            var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"));
            if (version < 1 || version > 3) throw new ArgumentException("Versión de respaldo no compatible. Usa una versión de Trazal que lo admita.");
            if (Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('trigger','view')")) != 0) throw new ArgumentException("El archivo contiene una estructura no reconocida.");
            var queries = new[] { "SELECT id,title,type,scope,vat,created FROM works LIMIT 0", "SELECT id,work_id,code,description,unit,quantity,price FROM concepts LIMIT 0", "SELECT key,value FROM settings LIMIT 0" };
            foreach (var query in queries) Execute(db, query);
            if (version >= 2) Execute(db, "SELECT id,work_id,date,category,description,amount,vat FROM expenses LIMIT 0");
            if (version >= 3)
            {
                Execute(db, "SELECT concept_id,date,quantity FROM planned_days LIMIT 0");
                Execute(db, "SELECT id,concept_id,date,quantity,note FROM progress LIMIT 0");
                Execute(db, "SELECT work_id,approved FROM baselines LIMIT 0");
            }
            using (var cmd = Command(db, "PRAGMA foreign_key_check")) using (var r = cmd.ExecuteReader())
                if (r.Read()) throw new ArgumentException("El respaldo contiene referencias incompletas.");
            if (Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM concepts c LEFT JOIN works w ON c.work_id=w.id WHERE w.id IS NULL")) != 0)
                throw new ArgumentException("Hay conceptos sin obra en el respaldo.");
            if (version >= 2 && Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM expenses e LEFT JOIN works w ON e.work_id=w.id WHERE w.id IS NULL")) != 0)
                throw new ArgumentException("Hay gastos sin obra en el respaldo.");
            if (version >= 3)
            {
                foreach(var table in new[]{"planned_days","progress"})
                    if(Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM "+table+" p LEFT JOIN concepts c ON p.concept_id=c.id WHERE c.id IS NULL"))!=0)
                        throw new ArgumentException("Hay registros sin concepto en el respaldo.");
                if(Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM baselines b LEFT JOIN works w ON b.work_id=w.id WHERE w.id IS NULL"))!=0)
                    throw new ArgumentException("Hay líneas base sin obra en el respaldo.");
            }
        }
        private void ValidateRestoredData(PreparedRestore result)
        {
            using (var db = Open()) CheckBackupSchema(db);
            var profile = GetProfile(); profile.Validate(); result.Owner = profile.FullName;
            var works = GetWorks(); result.WorkCount = works.Count; result.ConceptCount = 0; result.ExpenseCount = 0; result.ProgressCount = 0;
            foreach (var work in works)
            {
                work.Validate(); var concepts = GetConcepts(work.Id); var days = GetPlan(work.Id); var history = GetProgress(work.Id);
                if (concepts.GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new ArgumentException("Claves duplicadas en el respaldo.");
                foreach (var c in concepts)
                {
                    c.Validate(); Scheduling.ValidateDays(c, days.Where(p => p.ConceptId == c.Id).ToList(), IsFrozen(work.Id));
                    var entries = history.Where(p => p.ConceptId == c.Id).OrderBy(p => p.Date).ToList();
                    if (entries.Count > 0 && !IsFrozen(work.Id)) throw new ArgumentException("Hay avances sin programa fijado.");
                    ProgressEntry previous=null;
                    foreach (var entry in entries)
                    {
                        Scheduling.ValidateProgress(c, entry, new ProgressEntry[0]);
                        // Older cuts may equal their next neighbour after a retroactive insertion.
                        if(previous!=null && (entry.Date.Date==previous.Date.Date || entry.Quantity<previous.Quantity))
                            throw new ArgumentException("El respaldo contiene avances duplicados o decrecientes.");
                        previous=entry;
                    }
                }
                var expenses = GetExpenses(work.Id); foreach (var expense in expenses) expense.Validate();
                result.ConceptCount += concepts.Count; result.ExpenseCount += expenses.Count; result.ProgressCount += history.Count;
            }
        }
        public string Restore(PreparedRestore prepared)
        {
            if (prepared == null || !File.Exists(prepared.FilePath)) throw new ArgumentException("Vuelve a seleccionar el respaldo.");
            var staged = new LocalStore(prepared.FilePath); staged.ValidateRestoredData(prepared);
            var safety = Path.Combine(Path.GetDirectoryName(FilePath), "Antes-de-restaurar-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".trazal");
            Backup(safety); // Failure here never changes the active database.
            File.Replace(prepared.FilePath, FilePath, null); // Same volume, atomic replacement after validation.
            return safety;
        }
    }
}
