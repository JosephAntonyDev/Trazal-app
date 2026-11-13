using System;
using System.Linq;

namespace Trazal.Core
{
    public sealed class Expense
    {
        public static readonly string[] Categories = { "Materiales", "Mano de obra", "Maquinaria", "Subcontratos", "Servicios", "Gastos de oficina", "Gastos de oficina de campo", "Depreciación, mantenimiento y rentas", "Indirectos", "Otros gastos" };
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string WorkId { get; set; }
        public DateTime Date { get; set; } = DateTime.Today;
        public string Category { get; set; }
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public decimal Vat { get; set; }
        public decimal Tax => Budget.Round(Amount * Vat / 100m);
        public decimal Total => Amount + Tax;
        public void Validate()
        {
            Rules.Required(WorkId, "Obra", 32);
            Rules.Required(Description, "Descripción", 500);
            if (!Categories.Contains(Category)) throw new ArgumentException("Selecciona una categoría.");
            if (Date == DateTime.MinValue) throw new ArgumentException("Indica una fecha válida.");
            if (Amount <= 0 || Amount > 1000000000000m || Budget.Round(Amount) != Amount) throw new ArgumentException("Indica un importe positivo con hasta dos decimales.");
            if (Vat < 0 || Vat > 100 || Budget.Round(Vat) != Vat) throw new ArgumentException("El IVA debe estar entre 0 y 100, con hasta dos decimales.");
        }
    }
}
