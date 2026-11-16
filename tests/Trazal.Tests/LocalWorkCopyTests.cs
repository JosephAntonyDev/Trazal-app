using System;
using System.IO;
using System.Linq;
using System.Threading;
using Trazal.Core;
using Trazal.Data;
using Trazal.Desktop;

namespace Trazal.Tests
{
    internal static partial class Program
    {
        private static void LocalWorkCopyTests(string folder)
        {
            Check("Copia seleccionada conserva programa, avances, gastos, originales y perfil de destino", () => {
                var source = ReportFixture(Path.Combine(folder, "copy-source"));
                var work = source.GetWorks().Single();
                var extra = new Work { Title = "No seleccionada", Type = Work.Types[0] }; source.SaveWork(extra);
                var target = new LocalStore(Path.Combine(folder, "copy-target.db"));
                target.SaveProfile(new Profile { FirstName = "Cuenta de destino" });
                var original = source.CaptureSyncDocument(work.Id).ToJson();
                using (var prepared = target.PrepareLocalWorkCopy(source.FilePath))
                {
                    var result = target.CopyLocalWorks(prepared, new[] { work.Id }, "user-copy");
                    Equal(1, result.Copied); Equal(0, result.Skipped); Equal(1, target.GetWorks().Count);
                    Equal(original, target.CaptureSyncDocument(work.Id).ToJson());
                    Equal(original, source.CaptureSyncDocument(work.Id).ToJson()); Equal(2, source.GetWorks().Count);
                    Equal("Cuenta de destino", target.GetProfile().FirstName);
                    Equal(0, target.GetSyncStates().Count); Equal("0", target.SyncCursor);
                    Equal(true, File.Exists(result.BackupPath)); Equal(0, new LocalStore(result.BackupPath).GetWorks().Count);
                    var cloud = new FakeCloud { User = "user-copy" };
                    var sent = new SyncEngine(target, cloud, "user-copy").RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Equal(1, sent.Uploaded); Equal(original, cloud.Works[work.Id].Document.ToJson());
                }
            });
            Check("Reintentar copia no duplica ni reemplaza ediciones de la cuenta", () => {
                var source = ReportFixture(Path.Combine(folder, "retry-source")); var work = source.GetWorks().Single();
                var target = new LocalStore(Path.Combine(folder, "retry-copy.db"));
                using (var prepared = target.PrepareLocalWorkCopy(source.FilePath))
                {
                    target.CopyLocalWorks(prepared, new[] { work.Id }, "copy-user");
                    var expense = target.GetExpenses(work.Id).First(); expense.Description = "Editado en la cuenta"; target.SaveExpense(expense);
                    var changed = target.CaptureSyncDocument(work.Id).ToJson();
                    var result = target.CopyLocalWorks(prepared, new[] { work.Id, work.Id }, "copy-user");
                    Equal(0, result.Copied); Equal(1, result.Skipped); Equal(1, target.GetWorks().Count);
                    Equal(changed, target.CaptureSyncDocument(work.Id).ToJson());
                }
            });
            Check("Copia utiliza la instantánea revisada y cancelar conserva ambos espacios", () => {
                var source = ReportFixture(Path.Combine(folder, "snapshot-source")); var work = source.GetWorks().Single();
                var target = new LocalStore(Path.Combine(folder, "snapshot-copy.db"));
                var before = source.CaptureSyncDocument(work.Id).ToJson();
                using (var prepared = target.PrepareLocalWorkCopy(source.FilePath))
                {
                    var expense = source.GetExpenses(work.Id).First(); expense.Description = "Cambio posterior"; source.SaveExpense(expense);
                    target.CopyLocalWorks(prepared, new[] { work.Id }, "copy-user");
                    Equal(before, target.CaptureSyncDocument(work.Id).ToJson());
                    Equal(true, source.CaptureSyncDocument(work.Id).ToJson() != before);
                }
                var empty = new LocalStore(Path.Combine(folder, "cancel-copy.db"));
                using (empty.PrepareLocalWorkCopy(source.FilePath)) { }
                Equal(0, empty.GetWorks().Count); Equal(1, source.GetWorks().Count);
                Equal(0, Directory.GetDirectories(folder, "restore-*").Length);
            });
            Check("Colisión de conceptos revierte el lote completo sin modificar el destino", () => {
                var source = ReportFixture(Path.Combine(folder, "collision-source")); var work = source.GetWorks().Single();
                var first = new Work { Title = "Primera del lote", Type = Work.Types[0] }; source.SaveWork(first);
                var target = new LocalStore(Path.Combine(folder, "collision-copy.db"));
                var existing = new Work { Title = "Obra existente", Type = Work.Types[0] }; target.SaveWork(existing);
                var concept = source.GetConcepts(work.Id).First(); concept.WorkId = existing.Id; target.SaveConcept(concept);
                using (var prepared = target.PrepareLocalWorkCopy(source.FilePath))
                {
                    bool failed = false; try { target.CopyLocalWorks(prepared, new[] { first.Id, work.Id }, "copy-user"); } catch { failed = true; }
                    Equal(true, failed); Equal(1, target.GetWorks().Count); Equal(existing.Id, target.GetWorks().Single().Id);
                    Equal(1, target.GetConcepts(existing.Id).Count);
                }
            });
            Check("Copia rechaza selección vacía, ajena y destino diferente", () => {
                var source = ReportFixture(Path.Combine(folder, "invalid-copy-source")); var work = source.GetWorks().Single();
                var target = new LocalStore(Path.Combine(folder, "invalid-copy.db"));
                var other = new LocalStore(Path.Combine(folder, "other-copy.db"));
                using (var prepared = target.PrepareLocalWorkCopy(source.FilePath))
                {
                    Reject(() => target.CopyLocalWorks(prepared, new string[0], "copy-user"));
                    Reject(() => target.CopyLocalWorks(prepared, new[] { Guid.NewGuid().ToString("N") }, "copy-user"));
                    Reject(() => target.CopyLocalWorks(prepared, new[] { work.Id }, ""));
                    Reject(() => other.CopyLocalWorks(prepared, new[] { work.Id }, "copy-user"));
                    Equal(0, target.GetWorks().Count); Equal(0, other.GetWorks().Count);
                }
            });
            Check("Copia rechaza origen de otra cuenta y UID de destino distinto", () => {
                var source = ReportFixture(Path.Combine(folder, "bound-copy-source")); var work = source.GetWorks().Single();
                var target = new LocalStore(Path.Combine(folder, "bound-copy.db")); target.BindSyncAccount("owner", "https://sync.test");
                using (var prepared = target.PrepareLocalWorkCopy(source.FilePath))
                {
                    bool failed = false; try { target.CopyLocalWorks(prepared, new[] { work.Id }, "another"); } catch (InvalidOperationException) { failed = true; }
                    Equal(true, failed); Equal(0, target.GetWorks().Count);
                }
                source.BindSyncAccount("source-account", "https://sync.test");
                bool rejected = false; try { using (target.PrepareLocalWorkCopy(source.FilePath)) { } } catch (InvalidOperationException) { rejected = true; }
                Equal(true, rejected);
            });
            Check("Vista de copia exige selección, confirmación e identidad vigente", () => {
                var source = ReportFixture(Path.Combine(folder, "copy-ui-source")); var target = new LocalStore(Path.Combine(folder, "copy-ui.db"));
                bool current = true, busy = false;
                using (var view = new LocalWorkCopyViewModel(target.PrepareLocalWorkCopy(source.FilePath), target, "copy-user", "demo@example.com", () => current, value => busy = value, () => { }))
                {
                    Equal(false, view.Copy.CanExecute(null)); view.SelectAll.Execute(null); Equal(false, view.Copy.CanExecute(null));
                    view.Confirmed = true; Equal(true, view.Copy.CanExecute(null)); current = false; Equal(false, view.Copy.CanExecute(null));
                    view.CopyAsync().GetAwaiter().GetResult(); Equal(0, target.GetWorks().Count);
                    current = true; view.CopyAsync().GetAwaiter().GetResult(); Equal(1, target.GetWorks().Count);
                    Equal(false, busy); Equal(false, view.Copy.CanExecute(null)); Equal(true, view.HasBackup);
                }
                using (var view = new LocalWorkCopyViewModel(target.PrepareLocalWorkCopy(source.FilePath), target, "copy-user", "demo@example.com", () => true, _ => { }, () => { }))
                { Equal(false, view.Rows.Single().Available); view.SelectAll.Execute(null); Equal(false, view.Rows.Single().Include); }
            });
        }
    }
}
