using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using Trazal.Core;
using Trazal.Data;

namespace Trazal.Desktop
{
    public class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Notify([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(name); return true; }
    }
    public sealed class ActionCommand : ICommand
    {
        private readonly Action<object> execute;
        private readonly Func<object, bool> canExecute;
        public ActionCommand(Action<object> execute, Func<object, bool> canExecute = null) { this.execute = execute; this.canExecute = canExecute; }
        public bool CanExecute(object parameter) => canExecute == null || canExecute(parameter);
        public void Execute(object parameter) => execute(parameter);
        public event EventHandler CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
    }
    public sealed class Field : Observable
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Help { get; set; }
        public string Placeholder { get; set; } = "";
        public int MaxLength { get; set; } = 180;
        public string[] Choices { get; set; }
        public bool IsChoice => Choices != null;
        public string ComparableValue
        {
            get
            {
                decimal numeric;
                if ((Key == "vat" || Key == "quantity" || Key == "price" || Key == "amount") && decimal.TryParse(Value.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.GetCultureInfo("es-MX"), out numeric))
                    return numeric.ToString("G29", CultureInfo.InvariantCulture);
                return Value.Trim();
            }
        }
        private string value = "";
        public string Value { get => value; set => Set(ref this.value, value ?? ""); }
    }
    public sealed class Editor : Observable
    {
        public string Title { get; set; }
        public string Help { get; set; }
        public string SaveLabel { get; set; } = "Guardar";
        public ObservableCollection<Field> Fields { get; } = new ObservableCollection<Field>();
        public ICommand Save { get; private set; }
        public ICommand Cancel { get; private set; }
        private string error = "";
        public string Error { get => error; set => Set(ref error, value); }
        public string Value(string key) => Fields.Single(f => f.Key == key).Value.Trim();
        public void Configure(Action validate, Action save, Action cancel, bool editing)
        {
            var original = Fields.Select(f => f.ComparableValue).ToArray();
            Func<bool> valid = () => { try { validate(); return true; } catch (ArgumentException) { return false; } };
            Save = new ActionCommand(_ => { try { validate(); save(); } catch (Exception ex) { Error = ex.Message; } }, _ => valid() && (!editing || !Fields.Select(f => f.ComparableValue).SequenceEqual(original)));
            Cancel = new ActionCommand(_ => cancel());
            foreach (var f in Fields) f.PropertyChanged += (_, __) => { Error = ""; CommandManager.InvalidateRequerySuggested(); };
        }
        public void Add(string key, string label, string value = "", string help = "", int max = 180, string[] choices = null, string placeholder = "")
            => Fields.Add(new Field { Key = key, Label = label, Value = value, Help = help, MaxLength = max, Choices = choices, Placeholder = placeholder });
    }
    public sealed class WorkCard
    {
        public Work Work { get; set; }
        public string Title => Work.Title;
        public string Type => Work.Type;
        public string Scope => string.IsNullOrWhiteSpace(Work.Scope) ? "Alcance por definir" : Work.Scope;
        public string Amount => ShellViewModel.Money(Work.Subtotal);
        public string Count => Work.ConceptCount + (Work.ConceptCount == 1 ? " concepto" : " conceptos");
        public ICommand Open { get; set; }
    }
    public sealed class HomeViewModel : Observable
    {
        private readonly ShellViewModel shell;
        private readonly List<Work> works;
        private string search = "", type = "Todos los tipos";
        public ObservableCollection<WorkCard> Items { get; } = new ObservableCollection<WorkCard>();
        public string[] Types => new[] { "Todos los tipos" }.Concat(Work.Types).ToArray();
        public string Search { get => search; set { if (Set(ref search, value)) Filter(); } }
        public string Type { get => type; set { if (Set(ref type, value)) Filter(); } }
        public string Count => Items.Count + (Items.Count == 1 ? " obra" : " obras");
        public bool Empty => Items.Count == 0;
        public string EmptyTitle => works.Count == 0 ? "Tu próxima obra empieza aquí." : "No encontramos esa obra.";
        public string EmptyDescription => works.Count == 0 ? "Crea una obra y construye su presupuesto, concepto por concepto." : "Prueba con otro nombre, alcance o tipo de obra.";
        public string Total => ShellViewModel.Money(works.Sum(w => w.Subtotal));
        public ICommand Create => shell.CreateWork;
        public HomeViewModel(ShellViewModel shell, List<Work> works) { this.shell = shell; this.works = works; Filter(); }
        private void Filter()
        {
            Items.Clear();
            var compare = CultureInfo.GetCultureInfo("es-MX").CompareInfo;
            foreach (var w in works.Where(w => (type == "Todos los tipos" || w.Type == type) && compare.IndexOf(w.Title + " " + w.Type + " " + w.Scope, search ?? "", CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0))
                Items.Add(new WorkCard { Work = w, Open = new ActionCommand(_ => shell.OpenWork(w)) });
            Notify(nameof(Empty)); Notify(nameof(Count)); Notify(nameof(EmptyTitle)); Notify(nameof(EmptyDescription));
        }
    }
    public sealed class WorkViewModel
    {
        public Work Work { get; }
        public string Title => Work.Title;
        public string Type => Work.Type;
        public string Scope => string.IsNullOrWhiteSpace(Work.Scope) ? "Alcance por definir" : Work.Scope;
        public List<Concept> Concepts { get; }
        public BudgetTotal Totals { get; }
        public string Subtotal => ShellViewModel.Money(Totals.Subtotal);
        public string Tax => Totals.Tax.HasValue ? ShellViewModel.Money(Totals.Tax.Value) : "Por definir";
        public string Total => Totals.Total.HasValue ? ShellViewModel.Money(Totals.Total.Value) : "IVA pendiente";
        public string TaxLabel => Work.Vat.HasValue ? "IVA · " + Work.Vat.Value.ToString("0.##") + " %" : "IVA";
        public bool Empty => Concepts.Count == 0;
        public ICommand Expenses { get; }
        public ICommand Add { get; }
        public ICommand Edit { get; }
        public ICommand EditConcept { get; }
        public WorkViewModel(ShellViewModel shell, Work work, List<Concept> concepts)
        {
            Work = work; Concepts = concepts; Totals = Budget.Calculate(concepts, work.Vat);
            Expenses = new ActionCommand(_ => shell.OpenExpenses(work));
            Add = new ActionCommand(_ => shell.EditConcept(work, null));
            Edit = new ActionCommand(_ => shell.EditWork(work));
            EditConcept = new ActionCommand(c => shell.EditConcept(work, (Concept)c));
        }
    }
    public sealed class ProfileViewModel
    {
        public Profile Profile { get; set; }
        public Editor Editor { get; set; }
    }
    public sealed class ShellViewModel : Observable
    {
        private readonly LocalStore store;
        private Profile profile;
        private object page;
        private Editor dialog;
        private string theme, status = "", location = "Mis obras";
        private bool busy;
        public object Page
        {
            get => page;
            private set
            {
                if (!Set(ref page, value)) return;
                Notify(nameof(IsWorksSection));
                Notify(nameof(IsProfileSection));
            }
        }
        public bool IsWorksSection => Page is HomeViewModel || Page is WorkViewModel || Page is ExpensesViewModel;
        public bool IsProfileSection => Page is ProfileViewModel;
        public Editor Dialog { get => dialog; private set { Set(ref dialog, value); Notify(nameof(DialogOpen)); } }
        public bool DialogOpen => Dialog != null;
        public string Welcome => "Bienvenido, " + profile.FirstName;
        public string Initials => profile.Initials;
        public string Location { get => location; private set => Set(ref location, value); }
        public string Status { get => status; private set => Set(ref status, value); }
        public string[] Themes => new[] { "Sistema", "Claro", "Oscuro" };
        public string ThemeName { get => theme; set { if (!Themes.Contains(value)) return; if (Set(ref theme, value)) { Theme.Apply(value); Safe(() => store.SaveSetting("theme", value)); } } }
        public ICommand Home { get; }
        public ICommand ShowProfile { get; }
        public ICommand CreateWork { get; }
        public ICommand Backup { get; }
        public static string Money(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
        public ShellViewModel(LocalStore store)
        {
            this.store = store; profile = store.GetProfile(); theme = store.GetSetting("theme", "Sistema");
            Home = new ActionCommand(_ => Safe(OpenHome));
            ShowProfile = new ActionCommand(_ => OpenProfile());
            CreateWork = new ActionCommand(_ => EditWork(null));
            Backup = new ActionCommand(async _ => await SaveBackup(), _ => !busy);
            OpenHome();
        }
        private void Safe(Action action) { try { action(); } catch (Exception ex) { Status = "No se completó la operación: " + ex.Message; } }
        public void OpenHome() { Page = new HomeViewModel(this, store.GetWorks()); Location = "Mis obras"; }
        public void OpenWork(Work w) { Safe(() => { Page = new WorkViewModel(this, w, store.GetConcepts(w.Id)); Location = "Mi obra / Presupuesto"; }); }
        private static decimal Parse(string value, string label)
        {
            decimal result;
            if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.GetCultureInfo("es-MX"), out result))
                throw new ArgumentException("Revisa " + label + ". Usa punto decimal, sin separadores de miles.");
            return result;
        }
        public void EditWork(Work existing)
        {
            var e = new Editor { Title = existing == null ? "Crear una obra" : "Editar obra", Help = "Solo el título y el tipo son obligatorios. El IVA puede definirse después. Las fechas se establecerán en el programa de obra." };
            e.Add("title", "Título de la obra *", existing?.Title ?? "", max: 180, placeholder: "Ej. Banqueta en calle Reforma");
            e.Add("type", "Tipo de obra *", existing?.Type ?? "", choices: Work.Types, placeholder: "Selecciona un tipo de obra");
            e.Add("scope", "Dimensiones / alcance (opcional)", existing?.Scope ?? "", max: 240, placeholder: "Ej. 3 km o 250 m²");
            e.Add("vat", "IVA de la obra (%) · opcional", existing?.Vat?.ToString("0.####") ?? "", "Vacío: por definir. 0: sin IVA. Puedes editarlo después.", 8, placeholder: "Ej. 16");
            Func<Work> read = () => new Work { Id = existing?.Id ?? Guid.NewGuid().ToString("N"), Title = e.Value("title"), Type = e.Value("type"), Scope = e.Value("scope"), Vat = e.Value("vat") == "" ? (decimal?)null : Parse(e.Value("vat"), "el IVA"), CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow };
            e.Configure(() => read().Validate(), () => { var w = read(); store.SaveWork(w); Dialog = null; OpenWork(w); Status = existing == null ? "Obra creada y guardada en este equipo." : "Datos de la obra guardados."; }, () => Dialog = null, existing != null);
            Dialog = e;
        }
        public void EditConcept(Work work, Concept existing)
        {
            var e = new Editor { Title = existing == null ? "Agregar concepto" : "Editar concepto", Help = "El precio unitario pertenece a esta obra y se captura sin IVA. En esta primera versión se ingresa el precio final del concepto; el desglose de indirectos y utilidad se incorporará después." };
            e.Add("code", "Clave *", existing?.Code ?? "", max: 40, placeholder: "Ej. CON-001");
            e.Add("description", "Trabajo a realizar / descripción *", existing?.Description ?? "", max: 500, placeholder: "Ej. Construcción de banqueta de concreto");
            e.Add("unit", "Unidad *", existing?.Unit ?? "", max: 30, placeholder: "Ej. m², m³, kg, pieza o lote");
            e.Add("quantity", "Cantidad *", existing?.Quantity.ToString("0.####") ?? "", "Hasta cuatro decimales. Usa punto decimal.", 20, placeholder: "Ej. 100.50");
            e.Add("price", "Precio unitario sin IVA (MXN) *", existing?.UnitPrice.ToString("0.####") ?? "", "El precio puede ser 0; debe indicarse explícitamente.", 20, placeholder: "Ej. 1250.00");
            Func<Concept> read = () => new Concept { Id = existing?.Id ?? Guid.NewGuid().ToString("N"), WorkId = work.Id, Code = e.Value("code"), Description = e.Value("description"), Unit = e.Value("unit"), Quantity = Parse(e.Value("quantity"), "la cantidad"), UnitPrice = Parse(e.Value("price"), "el precio") };
            e.Configure(() => read().Validate(), () => { store.SaveConcept(read()); Dialog = null; OpenWork(work); Status = "Concepto guardado. Presupuesto actualizado."; }, () => Dialog = null, existing != null);
            Dialog = e;
        }
        public void OpenExpenses(Work work) => Safe(() => { Page = new ExpensesViewModel(this,work,store.GetExpenses(work.Id)); Location="Mi obra / Gastos"; });
        public void EditExpense(Work work, Expense existing)
        {
            var e=new Editor { Title=existing==null?"Registrar gasto":"Editar gasto", Help="Gastos independientes del presupuesto. Captura el importe sin IVA y su tasa por separado; 0 si no corresponde. Estos registros no modifican los conceptos ni el avance." };
            e.Add("date","Fecha (aaaa-mm-dd) *",(existing?.Date??DateTime.Today).ToString("yyyy-MM-dd"),placeholder:"Ej. 2026-11-13",max:10);
            e.Add("category","Categoría *",existing?.Category??"",choices:Expense.Categories,placeholder:"Selecciona una categoría");
            e.Add("description","Descripción del gasto *",existing?.Description??"",max:500,placeholder:"Ej. Pago de servicio topográfico o compra de papelería");
            e.Add("amount","Importe sin IVA (MXN) *",existing?.Amount.ToString("0.00")??"",max:18,placeholder:"Ej. 1500.00");
            e.Add("vat","IVA (%) *",(existing?.Vat??0).ToString("0.##"),"Indica 0 si no corresponde IVA.",8,placeholder:"Ej. 16");
            Func<Expense> read=()=> { DateTime date; if(!DateTime.TryParseExact(e.Value("date"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)) throw new ArgumentException("Escribe una fecha válida: aaaa-mm-dd."); return new Expense {Id=existing?.Id??Guid.NewGuid().ToString("N"),WorkId=work.Id,Date=date,Category=e.Value("category"),Description=e.Value("description"),Amount=Parse(e.Value("amount"),"el importe"),Vat=Parse(e.Value("vat"),"el IVA")}; };
            e.Configure(()=>read().Validate(),()=>{store.SaveExpense(read());Dialog=null;OpenExpenses(work);Status="Gasto guardado en esta obra.";},()=>Dialog=null,existing!=null);
            Dialog=e;
        }
        private void OpenProfile()
        {
            var e = new Editor { Title = "Datos personales", SaveLabel = "Guardar cambios" };
            e.Add("first", "Nombre *", profile.FirstName, max: 80); e.Add("last", "Apellido", profile.LastName, max: 100);
            e.Add("company", "Empresa", profile.Company, max: 140); e.Add("email", "Correo electrónico", profile.Email, max: 180); e.Add("phone", "Teléfono", profile.Phone, max: 30);
            Func<Profile> read = () => new Profile { FirstName = e.Value("first"), LastName = e.Value("last"), Company = e.Value("company"), Email = e.Value("email"), Phone = e.Value("phone") };
            e.Configure(() => read().Validate(), () => { var p = read(); store.SaveProfile(p); profile = p; Notify(nameof(Welcome)); Notify(nameof(Initials)); OpenProfile(); Status = "Perfil guardado en este equipo."; }, OpenHome, true);
            Page = new ProfileViewModel { Profile = profile, Editor = e }; Location = "Mi perfil";
        }
        private async Task SaveBackup()
        {
            var picker = new SaveFileDialog { Title = "Guardar respaldo de Trazal", Filter = "Respaldo Trazal (*.trazal)|*.trazal", FileName = "Trazal-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".trazal", OverwritePrompt = true };
            if (picker.ShowDialog() != true) return;
            busy = true; CommandManager.InvalidateRequerySuggested(); Status = "Preparando respaldo…";
            try { await Task.Run(() => store.Backup(picker.FileName)); Status = "Respaldo completo y verificado. Incluye obras y perfil local."; }
            catch (Exception ex) { Status = "No se pudo guardar el respaldo: " + ex.Message; }
            finally { busy = false; CommandManager.InvalidateRequerySuggested(); }
        }
    }
}
