using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Trazal.Core;
using Trazal.Data;
using Trazal.Desktop;

namespace Trazal.Tests
{
    internal static partial class Program
    {
        private static LocalStore ReportFixture(string folder)
        {
            Directory.CreateDirectory(folder);var db=new LocalStore(Path.Combine(folder,"report.db"));
            if(db.GetWorks().Count>0)return db;
            var w=new Work{Title="Banqueta de prueba · San José",Type=Work.Types[0],Scope="100 m²",Vat=16};db.SaveWork(w);
            var c=Item(w.Id,100,10);c.Code="0007";c.Description="Construcción de banqueta de concreto con acabado escobillado, cimbra, colocación, nivelación y curado. Incluye materiales, mano de obra y equipo.";db.SaveConcept(c);
            var start=DateTime.Today.AddDays(-9);db.SavePlan(w.Id,c.Id,Scheduling.Distribute(c,start,DateTime.Today));db.FreezeProgram(w.Id);
            db.AddProgress(w.Id,new ProgressEntry{ConceptId=c.Id,Date=start.AddDays(1),Quantity=20,Note="Primer corte"});
            db.AddProgress(w.Id,new ProgressEntry{ConceptId=c.Id,Date=start.AddDays(2),Quantity=40,Note="Medición acumulada, no sumar al corte anterior"});
            db.AddProgress(w.Id,new ProgressEntry{ConceptId=c.Id,Date=DateTime.Today,Quantity=100,Note="Concluido"});
            db.SaveExpense(new Expense{WorkId=w.Id,Date=start.AddDays(1),Category="Servicios",Description="Servicio topográfico del tramo de prueba",Amount=300,Vat=16});
            db.SaveExpense(new Expense{WorkId=w.Id,Date=start.AddDays(2),Category="Materiales",Description="Compra anticipada de cemento",Amount=200,Vat=16});
            db.SaveExpense(new Expense{WorkId=w.Id,Date=DateTime.Today,Category="Gastos de oficina",Description="=No es una fórmula + 1",Amount=50});
            db.SaveExpense(new Expense{WorkId=w.Id,Date=DateTime.Today.AddDays(1),Category="Servicios",Description="Fecha posterior al corte",Amount=9000});
            return db;
        }
        private static void ReportingTests(string folder)
        {
            var db=ReportFixture(Path.Combine(folder,"reports"));var w=db.GetWorks().Single();var cut=DateTime.Today.AddDays(-7);
            var report=WorkReport.Read(db,w.Id,cut);var p=report.Performance;
            Check("Curva: adelantado y gasto superior al ejecutado son estados independientes",()=>{Equal(300m,p.Planned);Equal(400m,p.Earned);Equal(500m,p.Spent);Equal(400m/300m,p.Spi.Value);Equal(.8m,p.Cpi.Value);Equal(40m,p.Percent.Value);});
            Check("Curva conserva programa completo y corta gasto y avance",()=>{Equal(1000m,p.Points.Last().Planned);Equal((decimal?)null,p.Points.Last().Earned);Equal((decimal?)null,p.Points.Last().Spent);Equal(0m,p.Points.First().Planned);Equal(2,report.Tables.Single(t=>t.Name=="Gastos").Rows.Count);});
            Check("Corte final usa último acumulado, no suma mediciones",()=>{var end=WorkReport.Read(db,w.Id,DateTime.Today).Performance;Equal(1000m,end.Earned);Equal(550m,end.Spent);Equal(1m,end.Spi.Value);Equal(100m,end.Percent.Value);});
            Check("Sin denominador los índices no inventan cero ni infinito",()=>{var before=WorkReport.Read(db,w.Id,DateTime.Today.AddYears(-1)).Performance;Equal((decimal?)null,before.Cpi);Equal((decimal?)null,before.Spi);var empty=Performance.Calculate(new Concept[0],new PlannedDay[0],new ProgressEntry[0],new Expense[0],cut);Equal((decimal?)null,empty.Percent);});
            Check("Redondeo de programa llega exactamente al importe contratado",()=>{var c=Item(w.Id,3,.3333m);var days=Scheduling.Distribute(c,cut.AddDays(-6),cut);var s=Performance.Calculate(new[]{c},days,new ProgressEntry[0],new Expense[0],cut);Equal(1m,s.Planned);});
            Check("Presupuesto cero mantiene valores finitos",()=>{var c=Item(w.Id,10,0);var s=Performance.Calculate(new[]{c},Scheduling.Distribute(c,cut,cut),new[]{new ProgressEntry{ConceptId=c.Id,Date=cut,Quantity=5}},new Expense[0],cut);Equal(0m,s.Earned);Equal((decimal?)null,s.Percent);});
            Check("Consulta y exportación no alteran línea base ni cantidades",()=>{var quantities=db.GetPlan(w.Id).Sum(d=>d.Quantity);var again=WorkReport.Read(db,w.Id,cut.AddDays(2));Equal(p.Points.Last().Planned,again.Performance.Points.Last().Planned);Equal(quantities,db.GetPlan(w.Id).Sum(d=>d.Quantity));Equal(true,db.IsFrozen(w.Id));});
            Check("Vista bloquea exportación sin corte y resalta sección Obras",()=>{var shell=new ShellViewModel(db);shell.OpenPerformance(w);Equal(true,shell.IsWorksSection);var view=(PerformanceViewModel)shell.Page;view.Cut=null;Equal(false,view.Pdf.CanExecute(null));Equal(false,view.Excel.CanExecute(null));view.Cut=cut;Equal(true,view.Pdf.CanExecute(null));Equal("0.80",view.Cpi);});
            var xlsx=Path.Combine(folder,"report.xlsx");
            Check("Excel real contiene ocho hojas, importes y claves con ceros",()=>{ExcelReport.Write(report,xlsx);var sheets=ExcelCatalog.Read(xlsx);Equal(8,sheets.Count);Equal("0007",sheets.Single(s=>s.Name=="Presupuesto").Cell(4,0));Equal("400",sheets[0].Rows.First(r=>r[0]=="Ejecutado al corte (EV)")[1]);Equal("500",sheets[0].Rows.First(r=>r[0]=="Gastos al corte (AC)")[1]);});
            Check("Excel trata descripciones como texto, incluso si empiezan con igual",()=>{ExcelReport.Write(WorkReport.Read(db,w.Id,DateTime.Today),xlsx);using(var zip=ZipFile.OpenRead(xlsx))using(var reader=new StreamReader(zip.GetEntry("xl/worksheets/sheet5.xml").Open())){var xml=reader.ReadToEnd();Equal(true,xml.Contains("=No es una fórmula"));Equal(false,xml.Contains("<f>"));}});
            Check("Fallo de exportación conserva el archivo anterior",()=>{var target=Path.Combine(folder,"existing.txt");File.WriteAllText(target,"anterior");try{WorkReport.WriteAtomically(target,temp=>{File.WriteAllText(temp,"parcial");throw new IOException("Prueba");});}catch(IOException){}Equal("anterior",File.ReadAllText(target));});
            Check("PDF genera documento local sin Office",()=>{var pdf=Path.Combine(folder,"report.pdf");PdfReport.Write(report,pdf);Equal("%PDF-",System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf),0,5));Equal(true,new FileInfo(pdf).Length>10000);});
            var backup=Path.Combine(folder,"valid.trazal");db.Backup(backup);var original=File.ReadAllBytes(backup);
            Check("Preparar y cancelar restauración no toca base ni respaldo",()=>{using(var preview=db.PrepareRestore(backup)){Equal(1,preview.WorkCount);Equal(3,preview.ProgressCount);Equal(4,preview.ExpenseCount);}Equal(true,original.SequenceEqual(File.ReadAllBytes(backup)));Equal(1,db.GetWorks().Count);});
            Check("Restaurar reemplaza todas las obras y conserva copia automática",()=>{var added=new Work{Title="Obra posterior",Type=Work.Types[0]};db.SaveWork(added);using(var preview=db.PrepareRestore(backup)){var before=db.Restore(preview);Equal(2,new LocalStore(before).GetWorks().Count);}Equal(1,db.GetWorks().Count);Equal(3,db.GetProgress(w.Id).Count);Equal(true,db.IsFrozen(w.Id));Equal(true,original.SequenceEqual(File.ReadAllBytes(backup)));});
            Check("Rechaza archivo inválido sin perder obras",()=>{var invalid=Path.Combine(folder,"invalid.trazal");File.WriteAllText(invalid,"no sqlite");bool rejected=false;try{using(var preview=db.PrepareRestore(invalid)){} }catch(Exception){rejected=true;}Equal(true,rejected);Equal(1,db.GetWorks().Count);});
            Check("Rechaza versión futura y auto-restauración",()=>{Reject(()=>db.PrepareRestore(db.FilePath));var future=Path.Combine(folder,"future.trazal");db.Backup(future);Sql(future,"PRAGMA user_version=999;");Reject(()=>db.PrepareRestore(future));Equal(1,db.GetWorks().Count);});
            Check("Rechaza esquema incompleto aunque declare versión válida",()=>{var broken=Path.Combine(folder,"broken.trazal");db.Backup(broken);Sql(broken,"DROP TABLE expenses;");bool rejected=false;try{using(var preview=db.PrepareRestore(broken)){} }catch(Exception){rejected=true;}Equal(true,rejected);Equal(4,db.GetExpenses(w.Id).Count);});
            Check("Restaura respaldo v2 migrando solo la copia de trabajo",()=>{var old=Path.Combine(folder,"old.trazal");db.Backup(old);Sql(old,"DROP TABLE progress;DROP TABLE planned_days;DROP TABLE baselines;PRAGMA user_version=2;");var bytes=File.ReadAllBytes(old);using(var preview=db.PrepareRestore(old)){Equal(0,preview.ProgressCount);db.Restore(preview);}Equal(true,bytes.SequenceEqual(File.ReadAllBytes(old)));Equal(4,db.GetExpenses(w.Id).Count);Equal(false,db.IsFrozen(w.Id));});
            Check("Borrador no presenta SPI o CPI como línea base aprobada",()=>{var view=new PerformanceViewModel(new ShellViewModel(db),db,w);Equal("Sin línea base",view.Spi);Equal("Sin línea base",view.Cpi);});
        }
        private static void Sql(string path,string sql)
        {using(var c=new System.Data.SQLite.SQLiteConnection("Data Source="+path+";Pooling=False")){c.Open();using(var cmd=c.CreateCommand()){cmd.CommandText=sql;cmd.ExecuteNonQuery();}}}
        private static void ReportsQa(string folder,bool ui)
        {
            var db=ReportFixture(folder);var work=db.GetWorks().First();var cut=DateTime.Today.AddDays(-7);var report=WorkReport.Read(db,work.Id,cut);
            PdfReport.Write(report,Path.Combine(folder,"reporte-prueba.pdf"));ExcelReport.Write(report,Path.Combine(folder,"reporte-prueba.xlsx"));db.Backup(Path.Combine(folder,"respaldo-prueba.trazal"));
            if(!ui)return;
            System.Threading.Thread.CurrentThread.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("es-MX");
            System.Windows.FrameworkElement.LanguageProperty.OverrideMetadata(typeof(System.Windows.FrameworkElement),new System.Windows.FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("es-MX")));
            var app=new System.Windows.Application();
            foreach(var name in new[]{"Styles.xaml","DateControls.xaml"})app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary{Source=new Uri("/Trazal;component/"+name,UriKind.Relative)});
            var shell=new ShellViewModel(db);Theme.Apply("Claro");shell.OpenPerformance(work);((PerformanceViewModel)shell.Page).Cut=cut;
            app.Run(new MainWindow{DataContext=shell,Title="Trazal · Revisión de reportes",Width=1280,Height=900});
        }
    }
}
