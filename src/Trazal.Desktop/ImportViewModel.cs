using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using Trazal.Core;
using Trazal.Data;

namespace Trazal.Desktop
{
    public sealed class ImportRow : Observable
    {
        private bool include;
        public bool Include { get=>include; set=>Set(ref include,value); }
        public int Row { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public string Quantity { get; set; }
        public string Price { get; set; }
        public string Error { get; set; }
        public Concept Concept { get; set; }
    }
    public sealed class ImportColumn : Observable
    {
        public string Label { get; set; }
        public string[] Choices { get; set; }
        private int index=-1;
        public int Index {get=>index;set=>Set(ref index,value);}
    }
    public sealed class ImportViewModel : Observable
    {
        private readonly ShellViewModel shell;
        private readonly LocalStore store;
        private readonly Work work;
        private List<CatalogSheet> sheets=new List<CatalogSheet>();
        private CatalogSheet sheet;
        private string fileName="Selecciona un archivo .xlsx o .xls",error="",header="1",first="2",last="";
        private bool nextRow,busy,reviewed;
        public string Title=>work.Title;
        public string FileName{get=>fileName;private set=>Set(ref fileName,value);}
        public string Error{get=>error;private set=>Set(ref error,value);}
        public List<CatalogSheet> Sheets=>sheets;
        public CatalogSheet Sheet{get=>sheet;set{if(Set(ref sheet,value)){Detect();}}}
        public ObservableCollection<ImportColumn> Columns{get;}=new ObservableCollection<ImportColumn>();
        public ObservableCollection<ImportRow> Rows{get;}=new ObservableCollection<ImportRow>();
        public string Header{get=>header;set{if(Set(ref header,value)){SetColumns();InvalidatePreview();}}}
        public string First{get=>first;set{if(Set(ref first,value))InvalidatePreview();}}
        public string Last{get=>last;set{if(Set(ref last,value))InvalidatePreview();}}
        public bool PriceNextRow{get=>nextRow;set{if(Set(ref nextRow,value))InvalidatePreview();}}
        public bool Reviewed{get=>reviewed;set{Set(ref reviewed,value);CommandManager.InvalidateRequerySuggested();}}
        public bool Ready=>!busy;
        public string Summary=>Rows.Count==0 ? "Prepara la vista previa para revisar las filas." : Rows.Count(r=>r.Include)+" seleccionadas · "+Rows.Count(r=>!r.Include)+" omitidas · "+Rows.Count(r=>r.Include&&r.Error!="")+" con errores · Subtotal válido: "+ShellViewModel.Money(Rows.Where(r=>r.Include&&r.Error=="").Sum(r=>r.Concept.Amount));
        public ICommand Choose{get;}
        public ICommand Preview{get;}
        public ICommand Import{get;}
        public ICommand Back{get;}
        public ImportViewModel(ShellViewModel shell,LocalStore store,Work work)
        {
            this.shell=shell;this.store=store;this.work=work;
            Choose=new ActionCommand(async _=>await ChooseFile(),_=>!busy);
            Preview=new ActionCommand(_=>BuildPreview(),_=>!busy&&Sheet!=null);
            Import=new ActionCommand(_=>Save(),_=>!busy&&Reviewed&&Rows.Any(r=>r.Include)&&Rows.All(r=>!r.Include||r.Error==""));
            Back=new ActionCommand(_=>shell.OpenWork(work),_=>!busy);
        }
        private async Task ChooseFile()
        {
            var picker=new OpenFileDialog{Title="Seleccionar catálogo de conceptos",Filter="Libro de Excel (*.xlsx;*.xls)|*.xlsx;*.xls",CheckFileExists=true};
            if(picker.ShowDialog()!=true)return;
            InvalidatePreview();
            busy=true;Notify(nameof(Ready));CommandManager.InvalidateRequerySuggested();Error="Leyendo archivo…";
            try{var loaded=await Task.Run(()=>ExcelCatalog.Read(picker.FileName));sheets=loaded;Notify(nameof(Sheets));FileName=Path.GetFileName(picker.FileName);Sheet=sheets.FirstOrDefault();Error="";}
            catch(Exception ex){Error="No se pudo leer el archivo: "+ex.Message;}
            finally{busy=false;Notify(nameof(Ready));CommandManager.InvalidateRequerySuggested();}
        }
        // Also used by integration tests: same workflow as the file picker.
        public void LoadSheets(List<CatalogSheet> source){sheets=source;Notify(nameof(Sheets));Sheet=sheets.FirstOrDefault();}
        private void Detect()
        {
            if(Sheet==null)return;
            int row=Sheet.Rows.FindIndex(r=>r.Any(v=>ExcelCatalog.Normalize(v)=="CODIGO"||ExcelCatalog.Normalize(v)=="CLAVE")&&r.Any(v=>ExcelCatalog.Normalize(v).Contains("CANTIDAD")));
            Header=(Math.Max(0,row)+1).ToString();First=(Math.Max(0,row)+2).ToString();Last=Sheet.Rows.Count.ToString();
            SetColumns(); PriceNextRow=false; InvalidatePreview();
        }
        private void SetColumns()
        {
            if(Sheet==null)return;
            int h;if(!int.TryParse(Header,out h)||h<1||h>Sheet.Rows.Count)return;
            string[] aliases={"CLAVE|CODIGO","DESCRIPCION|CONCEPTO","UNIDAD","CANTIDAD|VOLUMEN","PRECIOUNITARIO|PUNITARIO|PU"};
            string[] labels={"Clave *","Descripción *","Unidad *","Cantidad *","Precio unitario sin IVA *"};
            var choices=Enumerable.Range(0,Sheet.Width).Select(i=>Letter(i)+" · "+Sheet.Cell(h-1,i)).ToArray();
            Columns.Clear();
            for(int i=0;i<5;i++)
            {
                int found=Enumerable.Range(0,Sheet.Width).Where(c=>aliases[i].Split('|').Any(a=>ExcelCatalog.Normalize(Sheet.Cell(h-1,c)).StartsWith(a))).DefaultIfEmpty(-1).First();
                var column=new ImportColumn{Label=labels[i],Choices=choices,Index=found};
                column.PropertyChanged+=(_,__)=>InvalidatePreview();Columns.Add(column);
            }
        }
        private static string Letter(int i){var s="";for(i++;i>0;i=(i-1)/26)s=(char)('A'+(i-1)%26)+s;return s;}
        private void InvalidatePreview(){Rows.Clear();Reviewed=false;Error="";Notify(nameof(Summary));}
        public void BuildPreview()
        {
            InvalidatePreview();
            try
            {
                int from,to,h;
                if(Sheet==null||!int.TryParse(Header,out h)||h<1||h>Sheet.Rows.Count||!int.TryParse(First,out from)||!int.TryParse(Last,out to)||from<1||to<from||to>Sheet.Rows.Count)throw new ArgumentException("Revisa la fila del encabezado y la primera y última fila de datos.");
                var cols=Columns.Select(c=>c.Index).ToArray();
                if(cols.Length!=5||cols.Any(c=>c<0)||cols.Distinct().Count()!=5)throw new ArgumentException("Selecciona cinco columnas distintas.");
                var existing=new HashSet<string>(store.GetConcepts(work.Id).Select(c=>c.Code),StringComparer.OrdinalIgnoreCase);
                for(int i=from-1;i<to;i++)
                {
                    if(Sheet.Rows[i].All(string.IsNullOrWhiteSpace))continue;
                    var row=new ImportRow{Row=i+1,Code=Sheet.Cell(i,cols[0]),Description=Sheet.Cell(i,cols[1]),Unit=Sheet.Cell(i,cols[2]),Quantity=Sheet.Cell(i,cols[3]),Price=Sheet.Cell(i+(PriceNextRow?1:0),cols[4]),Error=""};
                    row.Include=!string.IsNullOrWhiteSpace(row.Code);
                    try{row.Concept=ExcelCatalog.Parse(Sheet,i,cols,PriceNextRow,work.Id);if(existing.Contains(row.Code))row.Error="La clave ya existe en esta obra.";}
                    catch(ArgumentException ex){row.Error=ex.Message;}
                    row.PropertyChanged+=(_,__)=>{Reviewed=false;Notify(nameof(Summary));};Rows.Add(row);
                }
                foreach(var group in Rows.Where(r=>!string.IsNullOrWhiteSpace(r.Code)).GroupBy(r=>r.Code,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
                    foreach(var row in group)row.Error="Clave repetida en el archivo. Corrige el Excel antes de importar.";
                Notify(nameof(Summary));CommandManager.InvalidateRequerySuggested();
            }
            catch(Exception ex){Error=ex.Message;}
        }
        private void Save()
        {
            if(!Import.CanExecute(null))return;
            try{store.ImportConcepts(work.Id,Rows.Where(r=>r.Include).Select(r=>r.Concept).ToList());shell.OpenWork(work);shell.SetStatus("Catálogo importado. Revisa el presupuesto y prepara el programa de obra.");}
            catch(Exception ex){Error=ex.Message;}
        }
    }
}
