using System;
using System.Globalization;
using System.Windows.Input;
using Trazal.Core;
using Trazal.Data;

namespace Trazal.Desktop
{
    public sealed class PerformanceViewModel : Observable
    {
        private readonly LocalStore store;
        public Work Work { get; }
        public bool Frozen { get; }
        private DateTime? cut = DateTime.Today;
        public DateTime? Cut { get => cut; set { if (Set(ref cut, value)) Refresh(); } }
        public PerformanceSnapshot Snapshot { get; private set; }
        public string Baseline => Frozen ? "Línea base fijada · MXN sin IVA" : "Programa en borrador · fija la línea base para comparar";
        public string Planned => Snapshot == null ? "—" : ShellViewModel.Money(Snapshot.Planned);
        public string Earned => Snapshot == null ? "—" : ShellViewModel.Money(Snapshot.Earned);
        public string Spent => Snapshot == null ? "—" : ShellViewModel.Money(Snapshot.Spent);
        public string Spi => Frozen ? Ratio(Snapshot?.Spi) : "Sin línea base";
        public string Cpi => Frozen ? Ratio(Snapshot?.Cpi) : "Sin línea base";
        public string Reading => Snapshot == null ? "Selecciona una fecha de corte." : !Frozen ? "Completa y fija el programa para habilitar los indicadores." :
            (Snapshot.Planned == 0 ? "Sin programa acumulado al corte" : Snapshot.Earned < Snapshot.Planned ? "Avance por debajo del programa" : Snapshot.Earned > Snapshot.Planned ? "Avance por encima del programa" : "Avance conforme al programa") +
            (Snapshot.Spent == 0 ? " · Sin gastos registrados." : Snapshot.Spent > Snapshot.Earned ? " · Gastos superiores al valor ejecutado." : " · Gastos dentro del valor ejecutado.");
        public string Percent => Snapshot?.Percent.HasValue == true ? Snapshot.Percent.Value.ToString("0.##") + " % del presupuesto ejecutado" : "Avance ponderado no disponible";
        public static string Ratio(decimal? value) => value.HasValue ? value.Value.ToString("0.00", CultureInfo.GetCultureInfo("es-MX")) : "No disponible";
        public ICommand Back { get; }
        public ICommand Program { get; }
        public ICommand Progress { get; }
        public ICommand Expenses { get; }
        public ICommand Pdf { get; }
        public ICommand Excel { get; }
        public PerformanceViewModel(ShellViewModel shell, LocalStore store, Work work)
        {
            this.store = store; Work = work; Frozen = store.IsFrozen(work.Id);
            Back = new ActionCommand(_ => shell.OpenWork(work));
            Program = new ActionCommand(_ => shell.OpenProgram(work));
            Progress = new ActionCommand(_ => shell.OpenProgram(work, true, null, Cut));
            Expenses = new ActionCommand(_ => shell.OpenExpenses(work));
            Pdf = new ActionCommand(async _ => await shell.ExportReport(work, Cut.Value, true), _ => Snapshot != null && !shell.IsBusy);
            Excel = new ActionCommand(async _ => await shell.ExportReport(work, Cut.Value, false), _ => Snapshot != null && !shell.IsBusy);
            Refresh();
        }
        private void Refresh()
        {
            Snapshot = Cut.HasValue ? Performance.Calculate(store.GetConcepts(Work.Id), store.GetPlan(Work.Id), store.GetProgress(Work.Id), store.GetExpenses(Work.Id), Cut.Value) : null;
            foreach (var name in new[] { "Snapshot", "Planned", "Earned", "Spent", "Spi", "Cpi", "Reading", "Percent" }) Notify(name);
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public sealed partial class ShellViewModel
    {
        public bool IsBusy => busy;
        public void OpenPerformance(Work work) => Safe(() => { Page = new PerformanceViewModel(this, store, work); Location = "Mi obra / Curva S y reportes"; });
    }
}
