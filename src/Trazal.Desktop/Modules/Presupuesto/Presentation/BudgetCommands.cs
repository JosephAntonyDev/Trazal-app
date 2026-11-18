using System;
using System.Linq;
using System.Windows.Input;
using Trazal.Core;
using Trazal.Desktop.Modules.Presupuesto.Presentation;

namespace Trazal.Desktop
{
    /// <summary>
    /// Partial class extension on ShellViewModel that adds the Presupuesto module navigation
    /// and its "Agregar concepto" / "Editar concepto" dialogs.
    /// All data is persisted locally via LocalStore (SQLite) — no backend required.
    /// </summary>
    public sealed partial class ShellViewModel
    {
        // ShowBudget property is declared in ViewModels.cs partial; initialized here via ConfigureBudget()
        public ICommand ShowBudget { get; private set; }
        public bool IsBudgetSection => Page is BudgetViewModel;

        internal void ConfigureBudget()
        {
            ShowBudget = new ActionCommand(_ => Safe(OpenBudget));
        }

        private void OpenBudget()
        {
            Page = new BudgetViewModel(this, store);
            Location = "Presupuesto";
        }

        /// <summary>
        /// Opens the "Agregar concepto" modal dialog from the Presupuesto view.
        /// The user picks the target work, then fills Clave/partida, Concepto,
        /// Cantidad, Unidad and P.U. deducido. Data is saved to LocalStore on confirm.
        /// </summary>
        public void BudgetAddConcept(BudgetViewModel budget)
        {
            var works = store.GetWorks();
            if (works.Count == 0)
            {
                Status = "Crea al menos una obra antes de agregar conceptos al presupuesto.";
                return;
            }
            var workTitles = works.Select(w => w.Title).ToArray();
            var e = new Editor
            {
                Title = "Agregar concepto",
                Help = "El precio unitario se captura sin IVA. La clave debe ser única dentro de cada obra."
            };
            e.Add("obra",        "Obra *",                workTitles[0], choices: workTitles,  placeholder: "Selecciona una obra");
            e.Add("code",        "Clave / partida *",     "",             max: 40,              placeholder: "Ej. CD-001");
            e.Add("description", "Concepto *",            "",             max: 500,             placeholder: "Ej. Limpieza del derecho de vía");
            e.Add("quantity",    "Cantidad *",            "",             "Hasta cuatro decimales. Usa punto decimal.", 20, placeholder: "Ej. 4.5");
            e.Add("unit",        "Unidad *",              "",             max: 30,              placeholder: "Ej. ha, m², m³, kg, pieza");
            e.Add("price",       "P.U. deducido (MXN) *", "",            "Precio unitario sin IVA. Usa punto decimal.", 20, placeholder: "Ej. 16800.00");

            Func<Work> selectedWork = () => works.First(w => w.Title == e.Value("obra"));
            Func<Concept> read = () =>
            {
                var w = selectedWork();
                return new Concept
                {
                    Id = Guid.NewGuid().ToString("N"),
                    WorkId = w.Id,
                    Code = e.Value("code"),
                    Description = e.Value("description"),
                    Unit = e.Value("unit"),
                    Quantity = Parse(e.Value("quantity"), "la cantidad"),
                    UnitPrice = Parse(e.Value("price"), "el P.U. deducido")
                };
            };
            e.Configure(
                () => read().Validate(),
                () => { store.SaveConcept(read()); Dialog = null; budget.Reload(); Status = "Concepto guardado en el presupuesto."; },
                () => Dialog = null,
                false);
            Dialog = e;
        }

        /// <summary>
        /// Opens the "Editar concepto" modal dialog pre-filled with the existing concept values.
        /// </summary>
        public void BudgetEditConcept(BudgetViewModel budget, Work work, Concept existing)
        {
            var e = new Editor
            {
                Title = "Editar concepto",
                Help = "El precio unitario se captura sin IVA. La clave debe ser única dentro de la obra."
            };
            e.Add("code",        "Clave / partida *",     existing.Code,                                         max: 40,  placeholder: "Ej. CD-001");
            e.Add("description", "Concepto *",            existing.Description,                                  max: 500, placeholder: "Ej. Limpieza del derecho de vía");
            e.Add("quantity",    "Cantidad *",            existing.Quantity.ToString("0.####"),  "Hasta cuatro decimales. Usa punto decimal.", 20, placeholder: "Ej. 4.5");
            e.Add("unit",        "Unidad *",              existing.Unit,                                         max: 30,  placeholder: "Ej. ha, m², m³, kg, pieza");
            e.Add("price",       "P.U. deducido (MXN) *", existing.UnitPrice.ToString("0.####"), "Precio unitario sin IVA. Usa punto decimal.", 20, placeholder: "Ej. 16800.00");

            Func<Concept> read = () => new Concept
            {
                Id = existing.Id,
                WorkId = work.Id,
                Code = e.Value("code"),
                Description = e.Value("description"),
                Unit = e.Value("unit"),
                Quantity = Parse(e.Value("quantity"), "la cantidad"),
                UnitPrice = Parse(e.Value("price"), "el P.U. deducido")
            };
            e.Configure(
                () => read().Validate(),
                () => { store.SaveConcept(read()); Dialog = null; budget.Reload(); Status = "Concepto actualizado en el presupuesto."; },
                () => Dialog = null,
                true);
            Dialog = e;
        }

    }
}
