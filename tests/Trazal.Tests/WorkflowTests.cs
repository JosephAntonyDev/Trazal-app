using System;
using System.Collections.Generic;
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
        private static void PreviewQa(string folder)
        {
            SeedQa(folder);
            System.Threading.Thread.CurrentThread.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("es-MX");
            System.Windows.FrameworkElement.LanguageProperty.OverrideMetadata(typeof(System.Windows.FrameworkElement),new System.Windows.FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("es-MX")));
            var app=new System.Windows.Application();
            app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary{Source=new Uri("/Trazal;component/Styles.xaml",UriKind.Relative)});
            app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary{Source=new Uri("/Trazal;component/DateControls.xaml",UriKind.Relative)});
            var db=new LocalStore(Path.Combine(folder,"trazal.db"));var shell=new ShellViewModel(db);
            Theme.Apply("Oscuro");shell.OpenWork(db.GetWorks().First());shell.EditExpense(db.GetWorks().First(),null);
            app.Run(new MainWindow{DataContext=shell,Title="Trazal · Revisión visual",Width=1280,Height=900});
        }
        private static void SeedQa(string folder)
        {
            Directory.CreateDirectory(folder);
            var db=new LocalStore(Path.Combine(folder,"trazal.db"));
            var w=new Work{Title="Prueba · Banqueta",Type=Work.Types[0],Vat=16};db.SaveWork(w);
            var c=Item(w.Id,100,250);c.Description="Construcción de banqueta de concreto";db.SaveConcept(c);
            db.SavePlan(w.Id,c.Id,Scheduling.Distribute(c,DateTime.Today.AddDays(-9),DateTime.Today));
            var file=Path.Combine(folder,"catalogo-prueba.xlsx");if(!File.Exists(file))CreateXlsx(file);
        }
        private static void Workflows(string folder)
        {
            var db=new LocalStore(Path.Combine(folder,"workflow.db"));
            var w=new Work{Title="Prueba de programa",Type=Work.Types[0]};db.SaveWork(w);
            var a=Item(w.Id,100,100);var b=Item(w.Id,3,10);b.Code="B";
            Check("Importación atómica rechaza duplicados sin guardar filas",()=>{Reject(()=>db.ImportConcepts(w.Id,new[]{a,Item(w.Id)}));Equal(0,db.GetConcepts(w.Id).Count);db.ImportConcepts(w.Id,new[]{a,b});var fresh=Item(w.Id);fresh.Code="NUEVO";Reject(()=>db.ImportConcepts(w.Id,new[]{fresh,Item(w.Id)}));Equal(2,db.GetConcepts(w.Id).Count);});
            Check("Números de Excel y separadores ambiguos",()=>{Equal(1234.50m,ExcelCatalog.Number("$1,234.50"));Equal(0m,ExcelCatalog.Number("0"));Reject(()=>ExcelCatalog.Number("12,50"));Reject(()=>ExcelCatalog.Number(""));});
            var sheet=new CatalogSheet{Name="Catálogo",Width=5};
            sheet.Rows.Add(new[]{"Clave","Descripción","Unidad","Cantidad","P. Unitario"});
            sheet.Rows.Add(new[]{"EX-001","Excavación de prueba","m³","10.5","20"});
            sheet.Rows.Add(new[]{"EX-002","Banqueta de prueba","m²","8",""});
            Check("Vista previa bloquea filas inválidas y exige revisar selección",()=>{
                var shell=new ShellViewModel(db);var view=new ImportViewModel(shell,db,w);view.LoadSheets(new List<CatalogSheet>{sheet});view.BuildPreview();Equal(2,view.Rows.Count);Equal(false,view.Import.CanExecute(null));view.Reviewed=true;Equal(false,view.Import.CanExecute(null));view.Rows[1].Include=false;Equal(false,view.Reviewed);view.Reviewed=true;Equal(true,view.Import.CanExecute(null));view.First="3";Equal(0,view.Rows.Count);Equal(false,view.Import.CanExecute(null));
            });
            Check("Precio en fila siguiente conserva clave y unidad",()=>{var s=new CatalogSheet{Name="Formato doble",Width=5};s.Rows.Add(new[]{"0001","Trabajo","m²","2",""});s.Rows.Add(new[]{"","","","","75.25"});var c=ExcelCatalog.Parse(s,0,new[]{0,1,2,3,4},true,w.Id);Equal("0001",c.Code);Equal(150.50m,c.Amount);});
            Check("Lee archivo XLSX real, texto, números y valor guardado de fórmula",()=>{
                var path=Path.Combine(folder,"catalog.xlsx");CreateXlsx(path);var s=ExcelCatalog.Read(path);Equal(1,s.Count);Equal("Catálogo",s[0].Name);var c=ExcelCatalog.Parse(s[0],1,new[]{0,1,2,3,4},false,w.Id);Equal("0007",c.Code);Equal(10.5m,c.Quantity);Equal(25m,c.UnitPrice);Equal(262.50m,c.Amount);
            });
            var start=DateTime.Today.AddDays(-9);var end=DateTime.Today;
            Check("Reparto diario conserva total y residuo exactos",()=>{var days=Scheduling.Distribute(b,start,start.AddDays(6));Equal(7,days.Count);Equal(3m,days.Sum(d=>d.Quantity));Equal(0.4290m,days.Last().Quantity);Reject(()=>Scheduling.Distribute(a,end,start));});
            Check("No permite fijar programa incompleto",()=>{Reject(()=>db.FreezeProgram(w.Id));db.SavePlan(w.Id,a.Id,new[]{new PlannedDay{ConceptId=a.Id,Date=start,Quantity=40}});Reject(()=>db.FreezeProgram(w.Id));Equal(false,db.IsFrozen(w.Id));});
            Check("Rechaza cantidades excesivas y fechas duplicadas sin perder borrador",()=>{Reject(()=>db.SavePlan(w.Id,a.Id,new[]{new PlannedDay{ConceptId=a.Id,Date=start,Quantity=101}}));Reject(()=>db.SavePlan(w.Id,a.Id,new[]{new PlannedDay{ConceptId=a.Id,Date=start,Quantity=20},new PlannedDay{ConceptId=a.Id,Date=start,Quantity=20}}));Equal(40m,db.GetPlan(w.Id).Sum(d=>d.Quantity));});
            Check("El avance requiere programa fijado",()=>Reject(()=>db.AddProgress(w.Id,new ProgressEntry{ConceptId=a.Id,Date=start,Quantity=10})));
            Check("Fijar protege catálogo y distribución incluso tras reabrir",()=>{
                db.SavePlan(w.Id,a.Id,Scheduling.Distribute(a,start,end));db.SavePlan(w.Id,b.Id,Scheduling.Distribute(b,start,end));db.FreezeProgram(w.Id);
                var reopened=new LocalStore(db.FilePath);Equal(true,reopened.IsFrozen(w.Id));a.UnitPrice=999;Reject(()=>reopened.SaveConcept(a));a.UnitPrice=100;Reject(()=>reopened.ImportConcepts(w.Id,new[]{Item(w.Id)}));Reject(()=>reopened.SavePlan(w.Id,a.Id,new PlannedDay[0]));Reject(()=>reopened.FreezeProgram(w.Id));Equal(100m,reopened.GetConcepts(w.Id).Single(c=>c.Id==a.Id).UnitPrice);Equal(103m,reopened.GetPlan(w.Id).Sum(d=>d.Quantity));
            });
            Check("Cortes acumulados no se suman y respetan fecha de consulta",()=>{
                db.AddProgress(w.Id,new ProgressEntry{ConceptId=a.Id,Date=start,Quantity=20});db.AddProgress(w.Id,new ProgressEntry{ConceptId=a.Id,Date=start.AddDays(5),Quantity=60});
                var history=db.GetProgress(w.Id);Equal(0m,Scheduling.ActualAt(history,start.AddDays(-1)));Equal(20m,Scheduling.ActualAt(history,start.AddDays(3)));Equal(60m,Scheduling.ActualAt(history,end));
            });
            Check("Registro retroactivo respeta ambos vecinos y rechaza fecha duplicada",()=>{
                Reject(()=>db.AddProgress(w.Id,new ProgressEntry{ConceptId=a.Id,Date=start.AddDays(2),Quantity=70}));Reject(()=>db.AddProgress(w.Id,new ProgressEntry{ConceptId=a.Id,Date=start.AddDays(2),Quantity=10}));
                db.AddProgress(w.Id,new ProgressEntry{ConceptId=a.Id,Date=start.AddDays(2),Quantity=40});Reject(()=>db.AddProgress(w.Id,new ProgressEntry{ConceptId=a.Id,Date=start.AddDays(2),Quantity=45}));Equal(3,db.GetProgress(w.Id).Count);
            });
            Check("Completa 100 por ciento y bloquea exceso o disminución",()=>{
                db.AddProgress(w.Id,new ProgressEntry{ConceptId=a.Id,Date=end.AddDays(-1),Quantity=100});
                Reject(()=>db.AddProgress(w.Id,new ProgressEntry{ConceptId=a.Id,Date=end,Quantity=101}));Reject(()=>db.AddProgress(w.Id,new ProgressEntry{ConceptId=a.Id,Date=end,Quantity=100}));Reject(()=>db.AddProgress(w.Id,new ProgressEntry{ConceptId=a.Id,Date=end,Quantity=99}));Equal(100m,Scheduling.ActualAt(db.GetProgress(w.Id),end));
            });
            Check("Avance futuro o de otra obra rechazado",()=>{
                Reject(()=>db.AddProgress(w.Id,new ProgressEntry{ConceptId=b.Id,Date=DateTime.Today.AddDays(1),Quantity=1}));var other=new Work{Title="Otra",Type=Work.Types[0]};db.SaveWork(other);var c=Item(other.Id);db.SaveConcept(c);Reject(()=>db.SavePlan(w.Id,c.Id,new PlannedDay[0]));Reject(()=>db.AddProgress(w.Id,new ProgressEntry{ConceptId=c.Id,Date=end,Quantity=1}));
            });
            Check("Captura porcentual se convierte en cantidad y bloquea 100.01",()=>{
                var shell=new ShellViewModel(db);shell.OpenProgram(w,true,b.Id);var view=(ProgramViewModel)shell.Page;shell.RecordProgress(view);var form=shell.Dialog;form.Fields.Single(f=>f.Key=="mode").Value="Porcentaje acumulado";var q=form.Fields.Single(f=>f.Key=="quantity");q.Value="100.01";Equal(false,form.Save.CanExecute(null));q.Value="33.3333";Equal(true,form.Save.CanExecute(null));form.Save.Execute(null);Equal(0.999999m,db.GetProgress(w.Id).Single(p=>p.ConceptId==b.Id).Quantity);Equal(103m,db.GetPlan(w.Id).Sum(d=>d.Quantity));
            });
            Check("Consulta al corte compara programa y real sin alterar gastos",()=>{
                var shell=new ShellViewModel(db);var view=new ProgramViewModel(shell,db,w,true,a.Id,start.AddDays(2));Equal(30m,view.Selected.Planned);Equal(40m,view.Selected.Actual);Equal("Adelantado",view.Selected.State);db.SaveExpense(new Expense{WorkId=w.Id,Category="Servicios",Description="Servicio",Amount=100});Equal(10030m,db.GetWorks().Single(x=>x.Id==w.Id).Subtotal);Equal(100m,db.GetExpenses(w.Id).Single().Total);
            });
            Check("Respaldo mantiene línea base y todo el historial",()=>{var path=Path.Combine(folder,"program.trazal");db.Backup(path);var restored=new LocalStore(path);Equal(true,restored.IsFrozen(w.Id));Equal(5,restored.GetProgress(w.Id).Count);Equal(103m,restored.GetPlan(w.Id).Sum(d=>d.Quantity));});
            Check("Migración v2 conserva gastos y crea respaldo previo",()=>{
                var path=Path.Combine(folder,"v2.db");var old=new LocalStore(path);old.SaveWork(w);old.SaveExpense(new Expense{WorkId=w.Id,Category="Servicios",Description="Anterior",Amount=50});
                using(var connection=new System.Data.SQLite.SQLiteConnection("Data Source="+path+";Pooling=False")){connection.Open();using(var cmd=connection.CreateCommand()){cmd.CommandText="DROP TABLE progress; DROP TABLE planned_days; DROP TABLE baselines; PRAGMA user_version=2;";cmd.ExecuteNonQuery();}}
                var migrated=new LocalStore(path);Equal(50m,migrated.GetExpenses(w.Id).Single().Total);Equal(false,migrated.IsFrozen(w.Id));Equal(1,Directory.GetFiles(folder,"v2.db.v2-*.trazal").Length);
            });
        }
        private static void CreateXlsx(string path)
        {
            using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
            {
                Action<string,string> add=(name,xml)=>{using(var writer=new StreamWriter(zip.CreateEntry(name).Open()))writer.Write(xml);};
                add("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                add("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                add("xl/workbook.xml","<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Catálogo\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                add("xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                add("xl/worksheets/sheet1.xml","<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Clave</t></is></c></row><row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>0007</t></is></c><c r=\"B2\" t=\"inlineStr\"><is><t>Trabajo sintético</t></is></c><c r=\"C2\" t=\"inlineStr\"><is><t>m²</t></is></c><c r=\"D2\"><v>10.5</v></c><c r=\"E2\"><f>5*5</f><v>25</v></c></row></sheetData></worksheet>");
            }
        }
    }
}
