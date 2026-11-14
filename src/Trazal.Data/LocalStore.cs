using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using Trazal.Core;

namespace Trazal.Data
{
    public sealed partial class LocalStore
    {
        public string FilePath { get; }
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public LocalStore(string path) : this(path, true) { }
        internal LocalStore(string path, bool migrationBackup)
        {
            FilePath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            using (var db = Open())
            {
                var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version"));
                if (version > 4) throw new InvalidOperationException("Estos datos requieren una versión más reciente de Trazal.");
                if (migrationBackup && version > 0 && version < 4) Backup(FilePath + ".v" + version + "-" + Guid.NewGuid().ToString("N") + ".trazal");
                using (var tx = db.BeginTransaction())
                {
                    Execute(db, @"CREATE TABLE IF NOT EXISTS works(id TEXT PRIMARY KEY,title TEXT NOT NULL,type TEXT NOT NULL,scope TEXT NOT NULL,vat TEXT,created TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS concepts(id TEXT PRIMARY KEY,work_id TEXT NOT NULL REFERENCES works(id),code TEXT NOT NULL COLLATE NOCASE,description TEXT NOT NULL,unit TEXT NOT NULL,quantity TEXT NOT NULL,price TEXT NOT NULL,UNIQUE(work_id,code));
CREATE INDEX IF NOT EXISTS ix_concepts_work ON concepts(work_id);
CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS expenses(id TEXT PRIMARY KEY,work_id TEXT NOT NULL REFERENCES works(id),date TEXT NOT NULL,category TEXT NOT NULL,description TEXT NOT NULL,amount TEXT NOT NULL,vat TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_expenses_work_date ON expenses(work_id,date);
CREATE TABLE IF NOT EXISTS planned_days(concept_id TEXT NOT NULL REFERENCES concepts(id),date TEXT NOT NULL,quantity TEXT NOT NULL,PRIMARY KEY(concept_id,date));
CREATE TABLE IF NOT EXISTS baselines(work_id TEXT PRIMARY KEY REFERENCES works(id),approved TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS progress(id TEXT PRIMARY KEY,concept_id TEXT NOT NULL REFERENCES concepts(id),date TEXT NOT NULL,quantity TEXT NOT NULL,note TEXT NOT NULL,UNIQUE(concept_id,date));
PRAGMA user_version=4;", tx);
                    Execute(db, SyncSchema, tx);
                    tx.Commit();
                }
            }
        }
        private SQLiteConnection Open()
        {
            var db = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = FilePath, ForeignKeys = true, Pooling = false, DefaultTimeout = 5 }.ToString());
            db.Open();
            return db;
        }
        private static SQLiteCommand Command(SQLiteConnection db, string sql, SQLiteTransaction tx = null, params object[] values)
        {
            var cmd = new SQLiteCommand(sql, db, tx);
            for (int i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("@p" + i, values[i] ?? DBNull.Value);
            return cmd;
        }
        private static string Number(decimal? value) => value.HasValue ? value.Value.ToString(Inv) : null;
        private static object Scalar(SQLiteConnection db, string sql, SQLiteTransaction tx = null, params object[] values)
        { using (var cmd = Command(db, sql, tx, values)) return cmd.ExecuteScalar(); }
        private static int Execute(SQLiteConnection db, string sql, SQLiteTransaction tx = null, params object[] values)
        { using (var cmd = Command(db, sql, tx, values)) return cmd.ExecuteNonQuery(); }
        public List<Work> GetWorks()
        {
            var items = new List<Work>();
            using (var db = Open())
            {
                using (var cmd = Command(db, "SELECT id,title,type,scope,vat,created FROM works ORDER BY created DESC"))
                using (var r = cmd.ExecuteReader()) while (r.Read())
                    items.Add(new Work { Id = r.GetString(0), Title = r.GetString(1), Type = r.GetString(2), Scope = r.GetString(3), Vat = r.IsDBNull(4) ? (decimal?)null : decimal.Parse(r.GetString(4), Inv), CreatedAt = DateTime.Parse(r.GetString(5), Inv, DateTimeStyles.RoundtripKind) });
                // Aggregate from decimal text in C#, never SQLite floating point SUM for money.
                var map = items.ToDictionary(w => w.Id);
                using (var cmd = Command(db, "SELECT work_id,quantity,price FROM concepts"))
                using (var r = cmd.ExecuteReader()) while (r.Read())
                {
                    var w = map[r.GetString(0)];
                    w.ConceptCount++;
                    w.Subtotal += Budget.Round(decimal.Parse(r.GetString(1), Inv) * decimal.Parse(r.GetString(2), Inv));
                }
            }
            return items;
        }
        public void SaveWork(Work w)
        {
            w.Validate();
            using (var db = Open()) using (var tx = db.BeginTransaction())
            {
                Execute(db, "INSERT OR IGNORE INTO works(id,title,type,scope,vat,created) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)", tx, w.Id, w.Title.Trim(), w.Type, (w.Scope ?? "").Trim(), Number(w.Vat), w.CreatedAt.ToString("o", Inv));
                Execute(db, "UPDATE works SET title=@p1,type=@p2,scope=@p3,vat=@p4 WHERE id=@p0", tx, w.Id, w.Title.Trim(), w.Type, (w.Scope ?? "").Trim(), Number(w.Vat));
                tx.Commit();
            }
        }
        public List<Concept> GetConcepts(string workId)
        {
            var list = new List<Concept>();
            using (var db = Open()) using (var cmd = Command(db, "SELECT id,code,description,unit,quantity,price FROM concepts WHERE work_id=@p0 ORDER BY rowid", null, workId))
            using (var r = cmd.ExecuteReader()) while (r.Read())
                list.Add(new Concept { Id = r.GetString(0), WorkId = workId, Code = r.GetString(1), Description = r.GetString(2), Unit = r.GetString(3), Quantity = decimal.Parse(r.GetString(4), Inv), UnitPrice = decimal.Parse(r.GetString(5), Inv) });
            return list;
        }
        public void SaveConcept(Concept c)
        {
            c.Validate();
            using (var db = Open()) using (var tx = db.BeginTransaction())
            {
                EnsureDraft(db, tx, c.WorkId);
                var owner = Scalar(db, "SELECT work_id FROM concepts WHERE id=@p0", tx, c.Id);
                if (owner != null && Convert.ToString(owner) != c.WorkId) throw new InvalidOperationException("No se puede mover un concepto a otra obra.");
                try
                {
                    var rows = Execute(db, "UPDATE concepts SET code=@p2,description=@p3,unit=@p4,quantity=@p5,price=@p6 WHERE id=@p0 AND work_id=@p1", tx, c.Id, c.WorkId, c.Code.Trim(), c.Description.Trim(), c.Unit.Trim(), Number(c.Quantity), Number(c.UnitPrice));
                    if (rows == 0) Execute(db, "INSERT INTO concepts(id,work_id,code,description,unit,quantity,price) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)", tx, c.Id, c.WorkId, c.Code.Trim(), c.Description.Trim(), c.Unit.Trim(), Number(c.Quantity), Number(c.UnitPrice));
                    tx.Commit();
                }
                catch (SQLiteException e) when (e.ResultCode == SQLiteErrorCode.Constraint)
                { throw new ArgumentException("La clave ya existe o la obra no está disponible. Revisa el concepto.", e); }
            }
        }
        public List<Expense> GetExpenses(string workId)
        {
            var list = new List<Expense>();
            using (var db = Open()) using (var cmd = Command(db, "SELECT id,date,category,description,amount,vat FROM expenses WHERE work_id=@p0 ORDER BY date DESC,rowid DESC", null, workId))
            using (var r = cmd.ExecuteReader()) while (r.Read())
                list.Add(new Expense { Id=r.GetString(0), WorkId=workId, Date=DateTime.ParseExact(r.GetString(1), "yyyy-MM-dd", Inv), Category=r.GetString(2), Description=r.GetString(3), Amount=decimal.Parse(r.GetString(4),Inv), Vat=decimal.Parse(r.GetString(5),Inv) });
            return list;
        }
        public void SaveExpense(Expense e)
        {
            e.Validate();
            using (var db=Open()) using (var tx=db.BeginTransaction())
            {
                var owner=Scalar(db,"SELECT work_id FROM expenses WHERE id=@p0",tx,e.Id);
                if(owner!=null && Convert.ToString(owner)!=e.WorkId) throw new InvalidOperationException("El gasto pertenece a otra obra.");
                var rows=Execute(db,"UPDATE expenses SET date=@p2,category=@p3,description=@p4,amount=@p5,vat=@p6 WHERE id=@p0 AND work_id=@p1",tx,e.Id,e.WorkId,e.Date.ToString("yyyy-MM-dd",Inv),e.Category,e.Description.Trim(),Number(e.Amount),Number(e.Vat));
                if(rows==0) Execute(db,"INSERT INTO expenses(id,work_id,date,category,description,amount,vat) VALUES(@p0,@p1,@p2,@p3,@p4,@p5,@p6)",tx,e.Id,e.WorkId,e.Date.ToString("yyyy-MM-dd",Inv),e.Category,e.Description.Trim(),Number(e.Amount),Number(e.Vat));
                tx.Commit();
            }
        }
        public string GetSetting(string key, string fallback = "")
        {
            using (var db = Open()) return Convert.ToString(Scalar(db, "SELECT value FROM settings WHERE key=@p0", null, key)) is string value && value.Length > 0 ? value : fallback;
        }
        public void SaveSetting(string key, string value)
        {
            using (var db = Open()) Execute(db, "INSERT OR REPLACE INTO settings(key,value) VALUES(@p0,@p1)", null, key, value ?? "");
        }
        public Profile GetProfile() => new Profile { FirstName = GetSetting("firstName", "Joseph"), LastName = GetSetting("lastName"), Email = GetSetting("email"), Phone = GetSetting("phone"), Company = GetSetting("company") };
        public void SaveProfile(Profile p)
        {
            p.Validate();
            using (var db = Open()) using (var tx = db.BeginTransaction())
            {
                var data = new Dictionary<string,string> { {"firstName",p.FirstName.Trim()}, {"lastName",p.LastName.Trim()}, {"email",p.Email.Trim()}, {"phone",p.Phone.Trim()}, {"company",p.Company.Trim()} };
                foreach (var item in data) Execute(db, "INSERT OR REPLACE INTO settings(key,value) VALUES(@p0,@p1)", tx, item.Key, item.Value);
                tx.Commit();
            }
        }
        public void Backup(string destination)
        {
            if (string.Equals(Path.GetFullPath(destination), FilePath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Elige otra ubicación para el respaldo.");
            var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination)), Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var source = Open())
                using (var copy = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = temp, Pooling = false }.ToString()))
                {
                    copy.Open();
                    source.BackupDatabase(copy, "main", "main", -1, null, 0);
                    if (Convert.ToString(Scalar(copy, "PRAGMA integrity_check")) != "ok") throw new IOException("No se pudo verificar el respaldo.");
                }
                if (File.Exists(destination)) File.Replace(temp, destination, null); else File.Move(temp, destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
