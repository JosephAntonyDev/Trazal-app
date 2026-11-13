using System;
using System.Collections.Generic;
using System.Linq;

namespace Trazal.Core
{
    public sealed class BudgetTotal
    {
        public decimal Subtotal { get; set; }
        public decimal? Tax { get; set; }
        public decimal? Total { get; set; }
    }
    public static class Budget
    {
        // Round each concept, then tax over the sum. This convention must be reconciled with client examples.
        public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        public static BudgetTotal Calculate(IEnumerable<Concept> concepts, decimal? vat)
        {
            if (vat.HasValue && (vat < 0 || vat > 100)) throw new ArgumentException("IVA fuera de rango.");
            var items = concepts.ToList();
            foreach (var c in items) c.Validate();
            var subtotal = items.Sum(c => c.Amount);
            decimal? tax = vat.HasValue ? Round(subtotal * vat.Value / 100m) : (decimal?)null;
            return new BudgetTotal { Subtotal = subtotal, Tax = tax, Total = tax.HasValue ? subtotal + tax.Value : (decimal?)null };
        }
    }
}
