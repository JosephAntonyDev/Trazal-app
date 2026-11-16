using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Trazal.Core;

namespace Trazal.Data
{
    public sealed class PreparedLocalWorkCopy : IDisposable
    {
        internal PreparedRestore Snapshot { get; }
        internal string Destination { get; }
        public IReadOnlyList<Work> Works { get; }
        internal PreparedLocalWorkCopy(PreparedRestore snapshot, string destination, List<Work> works)
        { Snapshot = snapshot; Destination = destination; Works = works.AsReadOnly(); }
        public void Dispose() { Snapshot.Dispose(); }
    }
    public sealed class LocalWorkCopyResult
    {
        public int Copied { get; internal set; }
        public int Skipped { get; internal set; }
        public string BackupPath { get; internal set; } = "";
    }
    public sealed partial class LocalStore
    {
        public PreparedLocalWorkCopy PrepareLocalWorkCopy(string sourcePath)
        {
            // PrepareRestore reads the source without modifying it and validates an isolated snapshot.
            var snapshot = PrepareRestore(sourcePath);
            try
            {
                var source = new LocalStore(snapshot.FilePath, false);
                using (var db = source.Open())
                    if (Scalar(db, "SELECT uid FROM cloud_identity WHERE id=1") != null)
                        throw new InvalidOperationException("El origen está vinculado a una cuenta. Solo puedes copiar desde el espacio local original.");
                return new PreparedLocalWorkCopy(snapshot, FilePath, source.GetWorks());
            }
            catch { snapshot.Dispose(); throw; }
        }
        public LocalWorkCopyResult CopyLocalWorks(PreparedLocalWorkCopy prepared, IEnumerable<string> workIds, string expectedUid)
        {
            if (prepared == null || prepared.Destination != FilePath || !File.Exists(prepared.Snapshot.FilePath))
                throw new ArgumentException("Vuelve a preparar la copia en esta cuenta.");
            if (string.IsNullOrWhiteSpace(expectedUid)) throw new ArgumentException("Inicia sesión antes de copiar obras.");
            var ids = (workIds ?? Enumerable.Empty<string>()).Distinct().ToList();
            if (ids.Count == 0 || ids.Any(id => !prepared.Works.Any(w => w.Id == id)))
                throw new ArgumentException("Selecciona al menos una obra de la lista.");
            var result = new LocalWorkCopyResult();
            // Keep an independent recovery point before any target mutation. Never copy profile or cloud state.
            result.BackupPath = Path.Combine(Path.GetDirectoryName(FilePath), "Antes-de-copiar-obras-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".trazal");
            Backup(result.BackupPath);
            using (var db = Open())
            {
                Execute(db, "ATTACH DATABASE @p0 AS local_copy", null, prepared.Snapshot.FilePath);
                using (var tx = db.BeginTransaction())
                {
                    var uid = Convert.ToString(Scalar(db, "SELECT uid FROM cloud_identity WHERE id=1", tx));
                    if (uid.Length > 0 && uid != expectedUid) throw new InvalidOperationException("La cuenta de destino cambió. No se copiaron obras.");
                    foreach (var id in ids)
                    {
                        // Stable work IDs make retries safe; an existing work is never replaced, even if edited.
                        if (Scalar(db, "SELECT id FROM works WHERE id=@p0", tx, id) != null) { result.Skipped++; continue; }
                        Execute(db, "INSERT INTO works(id,title,type,scope,vat,created) SELECT id,title,type,scope,vat,created FROM local_copy.works WHERE id=@p0", tx, id);
                        Execute(db, "INSERT INTO concepts(id,work_id,code,description,unit,quantity,price) SELECT id,work_id,code,description,unit,quantity,price FROM local_copy.concepts WHERE work_id=@p0", tx, id);
                        Execute(db, "INSERT INTO planned_days(concept_id,date,quantity) SELECT p.concept_id,p.date,p.quantity FROM local_copy.planned_days p JOIN local_copy.concepts c ON c.id=p.concept_id WHERE c.work_id=@p0", tx, id);
                        Execute(db, "INSERT INTO baselines(work_id,approved) SELECT work_id,approved FROM local_copy.baselines WHERE work_id=@p0", tx, id);
                        Execute(db, "INSERT INTO progress(id,concept_id,date,quantity,note) SELECT p.id,p.concept_id,p.date,p.quantity,p.note FROM local_copy.progress p JOIN local_copy.concepts c ON c.id=p.concept_id WHERE c.work_id=@p0", tx, id);
                        Execute(db, "INSERT INTO expenses(id,work_id,date,category,description,amount,vat) SELECT id,work_id,date,category,description,amount,vat FROM local_copy.expenses WHERE work_id=@p0", tx, id);
                        result.Copied++;
                    }
                    tx.Commit();
                }
            }
            return result;
        }
    }
}
