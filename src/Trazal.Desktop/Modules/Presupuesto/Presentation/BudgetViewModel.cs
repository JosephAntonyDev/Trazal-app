using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Trazal.Core;
using Trazal.Data;

namespace Trazal.Desktop.Modules.Presupuesto.Presentation
{
    public sealed class BudgetConcept
    {
        public Concept Concept { get; set; }
        public Work Work { get; set; }
        public string Code => Concept.Code;
        public string Description => Concept.Description;
        public string Quantity => Concept.Quantity.ToString("0.####", CultureInfo.GetCultureInfo("es-MX"));
        public string Unit => Concept.Unit;
        public string UnitPrice => Concept.UnitPrice.ToString("C4", CultureInfo.GetCultureInfo("es-MX"));
        public string Amount => ShellViewModel.Money(Concept.Amount);
        public string WorkTitle => Work.Title;
        public ICommand Edit { get; set; }
    }

    public sealed class BudgetViewModel : Observable
    {
        private readonly ShellViewModel shell;
        private readonly LocalStore store;
        private string search = "";
        private string workFilter = "Todas las obras";
        private List<BudgetConcept> allItems = new List<BudgetConcept>();

        public ObservableCollection<BudgetConcept> Items { get; } = new ObservableCollection<BudgetConcept>();
        public string[] WorkTitles { get; private set; } = new[] { "Todas las obras" };
        public string WorkFilter { get => workFilter; set { if (Set(ref workFilter, value)) Filter(); } }
        public string Search { get => search; set { if (Set(ref search, value)) Filter(); } }
        public string Count => Items.Count + (Items.Count == 1 ? " concepto" : " conceptos");
        public bool Empty => Items.Count == 0;
        public string TotalImporte => ShellViewModel.Money(Items.Sum(c => c.Concept.Amount));
        public ICommand Add { get; }

        public BudgetViewModel(ShellViewModel shell, LocalStore store)
        {
            this.shell = shell;
            this.store = store;
            Add = new ActionCommand(_ => shell.BudgetAddConcept(this));
            Reload();
        }

        public void Reload()
        {
            allItems.Clear();
            var works = store.GetWorks();
            WorkTitles = new[] { "Todas las obras" }.Concat(works.Select(w => w.Title)).ToArray();
            Notify(nameof(WorkTitles));
            foreach (var w in works)
            {
                var concepts = store.GetConcepts(w.Id);
                foreach (var c in concepts)
                    allItems.Add(new BudgetConcept
                    {
                        Concept = c,
                        Work = w,
                        Edit = new ActionCommand(_ => shell.BudgetEditConcept(this, w, c))
                    });
            }
            Filter();
        }

        private void Filter()
        {
            Items.Clear();
            var compare = CultureInfo.GetCultureInfo("es-MX").CompareInfo;
            foreach (var item in allItems)
            {
                if (workFilter != "Todas las obras" && item.WorkTitle != workFilter) continue;
                if (!string.IsNullOrEmpty(search) &&
                    compare.IndexOf(item.Code + " " + item.Description, search,
                        CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0) continue;
                Items.Add(item);
            }
            Notify(nameof(Count));
            Notify(nameof(Empty));
            Notify(nameof(TotalImporte));
        }
    }
}
