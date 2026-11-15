using System;
using System.IO;
using System.Linq;
using Trazal.Core;
using Trazal.Data;
using Trazal.Desktop;

namespace Trazal.Tests
{
    internal static partial class Program
    {
        private static int passed;
        private static void Check(string name, Action test) { test(); passed++; Console.WriteLine("OK  " + name); }
        private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception("Esperado: " + expected + "; resultado: " + actual); }
        private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Se aceptó un dato inválido."); }
        private static Concept Item(string work, decimal quantity = 100m, decimal price = 100m) => new Concept { WorkId = work, Code = "CON-001", Description = "Concreto", Unit = "m²", Quantity = quantity, UnitPrice = price };
        [STAThread]
        private static int Main(string[] args)
        {
            if(args.Length==3 && args[0]=="--sync-http") return HttpSyncQa(args[1],args[2]);
            if(args.Length==2 && args[0]=="--sync-ui") { PreviewSyncQa(args[1]); return 0; }
            if(args.Length==2 && args[0]=="--identity-ui") { PreviewIdentityQa(args[1]); return 0; }
            if(args.Length==2 && args[0]=="--qa-setup") { SeedQa(args[1]); return 0; }
            if(args.Length==2 && args[0]=="--ui-preview") { PreviewQa(args[1]); return 0; }
            if(args.Length==2 && (args[0]=="--reports-qa" || args[0]=="--reports-ui")) { ReportsQa(args[1],args[0]=="--reports-ui"); return 0; }
            var folder = Path.Combine(Path.GetTempPath(), "Trazal-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                Check("Título y tipo obligatorios", () => { Reject(() => new Work().Validate()); Reject(() => new Work { Title = "  ", Type = Work.Types[0] }.Validate()); Reject(() => new Work { Title = "Obra" }.Validate()); });
                Check("Obra mínima e IVA pendiente", () => new Work { Title = "Banqueta", Type = Work.Types[0] }.Validate());
                Check("IVA fuera de rango", () => Reject(() => new Work { Title = "Obra", Type = Work.Types[0], Vat = 101 }.Validate()));
                Check("Presupuesto exacto de 100 x 100 más IVA", () => { var t = Budget.Calculate(new[] { Item("a") }, 16); Equal(10000m, t.Subtotal); Equal((decimal?)1600m, t.Tax); Equal((decimal?)11600m, t.Total); });
                Check("IVA desconocido diferente a cero", () => { Equal((decimal?)null, Budget.Calculate(new[] { Item("a") }, null).Total); Equal((decimal?)10000m, Budget.Calculate(new[] { Item("a") }, 0).Total); });
                Check("Redondeo por concepto y después impuesto", () => { var a = Item("a", 3, 0.335m); Equal(1.01m, a.Amount); Equal((decimal?)1.17m, Budget.Calculate(new[] { a }, 16).Total); });
                Check("Cantidades negativas y precisión inválida", () => { Reject(() => Item("a", -1).Validate()); Reject(() => Item("a", 0).Validate()); Reject(() => Item("a", 1.00001m).Validate()); Reject(() => Item("a", 1, -1).Validate()); });
                Check("Precio cero explícito", () => Item("a", 1, 0).Validate());
                Check("Perfil y correo inválido", () => { Reject(() => new Profile { FirstName = " " }.Validate()); Reject(() => new Profile { Email = "sin-arroba" }.Validate()); });
                var file = Path.Combine(folder, "test.db");
                var db = new LocalStore(file);
                var w = new Work { Title = "Banqueta", Type = Work.Types[0] };
                var w2 = new Work { Title = "Escuela", Type = Work.Types[2], Vat = 0 };
                Check("Persistencia de obras tras reabrir", () => { db.SaveWork(w); db.SaveWork(w2); var reopened = new LocalStore(file); Equal(2, reopened.GetWorks().Count); Equal((decimal?)null, reopened.GetWorks().Single(x => x.Id == w.Id).Vat); Equal((decimal?)0, reopened.GetWorks().Single(x => x.Id == w2.Id).Vat); });
                var c = Item(w.Id); var c2 = Item(w2.Id, 5, 123.4567m);
                Check("Misma clave, precios independientes por obra", () => { db.SaveConcept(c); db.SaveConcept(c2); Equal(100m, db.GetConcepts(w.Id).Single().UnitPrice); Equal(123.4567m, db.GetConcepts(w2.Id).Single().UnitPrice); });
                Check("Editar obra conserva conceptos", () => { w.Title = "Banqueta actualizada"; w.Vat = 16; db.SaveWork(w); Equal(1, db.GetConcepts(w.Id).Count); });
                Check("Duplicado rechazado sin alterar presupuesto", () => { Reject(() => db.SaveConcept(Item(w.Id, 999))); Equal(10000m, db.GetWorks().Single(x => x.Id == w.Id).Subtotal); });
                Check("Editar concepto actualiza solo su obra", () => { c.UnitPrice = 120; db.SaveConcept(c); Equal(12000m, db.GetWorks().Single(x => x.Id == w.Id).Subtotal); Equal(617.28m, db.GetWorks().Single(x => x.Id == w2.Id).Subtotal); });
                Check("Rechaza mover concepto entre obras", () => { c.WorkId = w2.Id; try { db.SaveConcept(c); throw new Exception("Movimiento aceptado"); } catch (InvalidOperationException) { c.WorkId = w.Id; } });
                Check("Perfil persiste y conserva caracteres", () => { db.SaveProfile(new Profile { FirstName = "José", LastName = "Muñoz", Company = "O'Brien", Email = "test@example.com" }); Equal("José Muñoz", new LocalStore(file).GetProfile().FullName); });
                Check("Respaldo íntegro, reabrible e independiente", () => { var backup = Path.Combine(folder, "copia.trazal"); db.Backup(backup); var recovered = new LocalStore(backup); Equal(2, recovered.GetWorks().Count); Equal(12000m, recovered.GetWorks().Single(x => x.Id == w.Id).Subtotal); Equal("José", recovered.GetProfile().FirstName); c.UnitPrice = 130; db.SaveConcept(c); Equal(120m, recovered.GetConcepts(w.Id).Single().UnitPrice); db.Backup(backup); Equal(130m, new LocalStore(backup).GetConcepts(w.Id).Single().UnitPrice); });
                Check("No sobrescribe base activa al respaldar", () => Reject(() => db.Backup(file)));
                var shell = new ShellViewModel(db);
                Check("Crear exige solo título y tipo", () => { shell.CreateWork.Execute(null); var form = shell.Dialog; Equal(false, form.Save.CanExecute(null)); form.Fields.Single(f => f.Key == "title").Value = "Obra mínima"; Equal(false, form.Save.CanExecute(null)); form.Fields.Single(f => f.Key == "type").Value = Work.Types[0]; Equal(true, form.Save.CanExecute(null)); form.Save.Execute(null); Equal(false, shell.DialogOpen); Equal(3, db.GetWorks().Count); });
                Check("Editar sin cambios, revertir y normalizar IVA", () => { shell.EditWork(w); var form = shell.Dialog; Equal(false, form.Save.CanExecute(null)); var title = form.Fields.Single(f => f.Key == "title"); title.Value += " nueva"; Equal(true, form.Save.CanExecute(null)); title.Value = w.Title; Equal(false, form.Save.CanExecute(null)); form.Fields.Single(f => f.Key == "vat").Value = "16.00"; Equal(false, form.Save.CanExecute(null)); form.Fields.Single(f => f.Key == "vat").Value = ""; Equal(true, form.Save.CanExecute(null)); form.Cancel.Execute(null); });
                Check("Perfil bloquea correo inválido y vuelve a bloquear al guardar", () => { shell.ShowProfile.Execute(null); var form = ((ProfileViewModel)shell.Page).Editor; Equal(false, form.Save.CanExecute(null)); var email = form.Fields.Single(f => f.Key == "email"); email.Value = "mal"; Equal(false, form.Save.CanExecute(null)); email.Value = "otro@example.com"; Equal(true, form.Save.CanExecute(null)); form.Save.Execute(null); Equal(false, ((ProfileViewModel)shell.Page).Editor.Save.CanExecute(null)); });
                Check("Buscar ignora acentos y combina filtro de tipo", () => { db.SaveWork(new Work { Title = "Drenáje de prueba", Type = Work.Types[1] }); shell.Home.Execute(null); var home = (HomeViewModel)shell.Page; home.Search = "DRENAJE"; Equal(1, home.Items.Count); home.Type = Work.Types[2]; Equal(0, home.Items.Count); });
                var expense = new Expense { WorkId=w.Id,Date=new DateTime(2026,11,13),Category="Servicios",Description="Servicio topográfico",Amount=1500m,Vat=16m };
                Check("Gastos independientes del presupuesto y de otras obras",()=>{db.SaveExpense(expense);Equal(1,db.GetExpenses(w.Id).Count);Equal(0,db.GetExpenses(w2.Id).Count);Equal(13000m,db.GetWorks().Single(x=>x.Id==w.Id).Subtotal);Equal(1740m,db.GetExpenses(w.Id).Single().Total);});
                Check("Editar categoría e importe persiste sin duplicar",()=>{expense.Category="Gastos de oficina";expense.Amount=200;expense.Vat=0;db.SaveExpense(expense);var entries=new LocalStore(file).GetExpenses(w.Id);Equal(1,entries.Count);Equal(200m,entries.Single().Total);Equal("Gastos de oficina",entries.Single().Category);});
                Check("Validación de categorías e importes",()=>{Reject(()=>new Expense {WorkId=w.Id,Description="Prueba",Category="Servicios",Amount=-1}.Validate());Reject(()=>new Expense {WorkId=w.Id,Description="Prueba",Category="Desconocida",Amount=1}.Validate());Reject(()=>new Expense {WorkId=w.Id,Description="Prueba",Category="Servicios",Amount=1.001m}.Validate());});
                Check("Filtrar gastos no modifica totales de otra categoría",()=>{db.SaveExpense(new Expense {WorkId=w.Id,Category="Servicios",Description="Otro servicio",Amount=100,Vat=16});var view=new ExpensesViewModel(shell,w,db.GetExpenses(w.Id));Equal(2,view.Items.Count);view.Category="Servicios";Equal(1,view.Items.Count);Equal(ShellViewModel.Money(116),view.Total);});
                Check("Respaldo conserva los gastos",()=>{var backup=Path.Combine(folder,"gastos.trazal");db.Backup(backup);Equal(2,new LocalStore(backup).GetExpenses(w.Id).Count);});
                Check("Migración v1 conserva obras y crea copia previa",()=>{
                    var legacy=Path.Combine(folder,"legacy.db");var old=new LocalStore(legacy);old.SaveWork(w);
                    using(var connection=new System.Data.SQLite.SQLiteConnection("Data Source="+legacy+";Pooling=False")) {connection.Open();using(var cmd=connection.CreateCommand()){cmd.CommandText="DROP TABLE expenses; PRAGMA user_version=1;";cmd.ExecuteNonQuery();}}
                    var migrated=new LocalStore(legacy);Equal(1,migrated.GetWorks().Count);Equal(0,migrated.GetExpenses(w.Id).Count);Equal(1,Directory.GetFiles(folder,"legacy.db.v1-*.trazal").Length);
                    migrated.SaveExpense(expense);Equal(1,new LocalStore(legacy).GetExpenses(w.Id).Count);
                });
                Check("Gasto con calendario e IVA vacío guarda cero y exige fecha",()=>{
                    shell.EditExpense(w2,null);var form=shell.Dialog;
                    form.Fields.Single(f=>f.Key=="category").Value="Servicios";
                    form.Fields.Single(f=>f.Key=="description").Value="Servicio de prueba";
                    form.Fields.Single(f=>f.Key=="amount").Value="100.00";
                    var date=form.Fields.Single(f=>f.Key=="date");date.DateValue=null;Equal(false,form.Save.CanExecute(null));
                    date.DateValue=new DateTime(2026,11,13);Equal(true,form.Save.CanExecute(null));form.Save.Execute(null);
                    var savedExpense=db.GetExpenses(w2.Id).Single();Equal(0m,savedExpense.Vat);Equal(100m,savedExpense.Total);Equal(new DateTime(2026,11,13),savedExpense.Date);
                    shell.EditExpense(w2,savedExpense);shell.Dialog.Fields.Single(f=>f.Key=="vat").Value="";Equal(false,shell.Dialog.Save.CanExecute(null));shell.Dialog.Cancel.Execute(null);
                });
                Workflows(folder); ReportingTests(folder); SyncTests(folder); IdentityTests(folder);
                Console.WriteLine(passed + " pruebas aprobadas."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }
    }
}
