using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Trazal.Core;
using Trazal.Data;

namespace Trazal.Desktop.Modules.Programa.Presentation
{
    /// <summary>
    /// Una fila del programa global: un concepto con sus fechas y el importe distribuido por mes.
    /// </summary>
    public sealed class ProgramaRow
    {
        public Concept Concept { get; set; }
        public Work Work { get; set; }
        public List<PlannedDay> Days { get; set; }

        public string Code => Concept.Code;
        public string Description => Concept.Description;
        public string Unit => Concept.Unit;
        public string WorkTitle => Work.Title;

        // Inicio y término calculados del plan de días
        public DateTime? StartDate => Days.Count > 0 ? Days.Min(d => d.Date) : (DateTime?)null;
        public DateTime? EndDate   => Days.Count > 0 ? Days.Max(d => d.Date) : (DateTime?)null;

        public string Period =>
            StartDate.HasValue && EndDate.HasValue
                ? StartDate.Value.ToString("d-MMM", CultureInfo.GetCultureInfo("es-MX")) +
                  " — " +
                  EndDate.Value.ToString("d-MMM", CultureInfo.GetCultureInfo("es-MX"))
                : "Sin programar";

        /// <summary>Importe programado en un mes dado (año+mes).</summary>
        public decimal AmountForMonth(int year, int month)
        {
            if (Concept.Quantity == 0) return 0;
            var qty = Days
                .Where(d => d.Date.Year == year && d.Date.Month == month)
                .Sum(d => d.Quantity);
            return Budget.Round(Concept.Amount * qty / Concept.Quantity);
        }

        public ICommand Reschedule { get; set; }
    }

    /// <summary>
    /// Columna de mes dinámica para el DataGrid del programa global.
    /// </summary>
    public sealed class MonthColumn
    {
        public int Year  { get; set; }
        public int Month { get; set; }
        public string Header => new DateTime(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-MX"));
    }

    /// <summary>
    /// ViewModel del módulo Programa global — muestra todos los conceptos de todas
    /// las obras con su período programado e importes mensuales.
    /// Solo lectura; para editar el programa se abre la obra desde "Mis obras".
    /// </summary>
    public sealed class ProgramaViewModel : Observable
    {
        private readonly ShellViewModel shell;
        private readonly LocalStore store;

        private string search = "";
        private string workFilter = "Todas las obras";
        private List<ProgramaRow> allRows = new List<ProgramaRow>();

        public ObservableCollection<ProgramaRow> Items { get; } = new ObservableCollection<ProgramaRow>();
        public ObservableCollection<MonthColumn> Months { get; } = new ObservableCollection<MonthColumn>();

        public string[] WorkTitles { get; private set; } = new[] { "Todas las obras" };
        public string WorkFilter { get => workFilter; set { if (Set(ref workFilter, value)) Filter(); } }
        public string Search     { get => search;     set { if (Set(ref search,     value)) Filter(); } }

        public string Count => Items.Count + (Items.Count == 1 ? " concepto" : " conceptos");
        public bool   Empty => Items.Count == 0;

        public ICommand Refresh { get; }

        public ProgramaViewModel(ShellViewModel shell, LocalStore store)
        {
            this.shell = shell;
            this.store = store;
            Refresh = new ActionCommand(_ => Reload());
            Reload();
        }

        public void Reload()
        {
            allRows.Clear();
            var works = store.GetWorks();
            WorkTitles = new[] { "Todas las obras" }.Concat(works.Select(w => w.Title)).ToArray();
            Notify(nameof(WorkTitles));

            foreach (var w in works)
            {
                var concepts = store.GetConcepts(w.Id);
                var plan     = store.GetPlan(w.Id);

                foreach (var c in concepts)
                {
                    var days = plan.Where(d => d.ConceptId == c.Id).ToList();
                    allRows.Add(new ProgramaRow
                    {
                        Concept     = c,
                        Work        = w,
                        Days        = days,
                        Reschedule  = new ActionCommand(_ => shell.OpenWork(w))
                    });
                }
            }

            RebuildMonths();
            Filter();
        }

        /// <summary>
        /// Calcula las columnas de meses que cubre el plan completo.
        /// </summary>
        private void RebuildMonths()
        {
            Months.Clear();
            var allDays = allRows.SelectMany(r => r.Days).ToList();
            if (allDays.Count == 0) return;

            var first = new DateTime(allDays.Min(d => d.Date).Year, allDays.Min(d => d.Date).Month, 1);
            var last  = new DateTime(allDays.Max(d => d.Date).Year, allDays.Max(d => d.Date).Month, 1);
            for (var m = first; m <= last; m = m.AddMonths(1))
                Months.Add(new MonthColumn { Year = m.Year, Month = m.Month });
        }

        private void Filter()
        {
            Items.Clear();
            var cmp = CultureInfo.GetCultureInfo("es-MX").CompareInfo;
            foreach (var row in allRows)
            {
                if (workFilter != "Todas las obras" && row.WorkTitle != workFilter) continue;
                if (!string.IsNullOrEmpty(search) &&
                    cmp.IndexOf(row.Code + " " + row.Description, search,
                        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0) continue;
                Items.Add(row);
            }
            Notify(nameof(Count));
            Notify(nameof(Empty));
        }
    }
}
