using System;
using System.Collections.Generic;
using System.Linq;

namespace Trazal.Core
{
    public sealed class PlannedDay
    {
        public string ConceptId { get; set; }
        public DateTime Date { get; set; }
        public decimal Quantity { get; set; }
    }

    public sealed class ProgressEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ConceptId { get; set; }
        public DateTime Date { get; set; }
        public decimal Quantity { get; set; }
        public string Note { get; set; } = "";
    }

    public static class Scheduling
    {
        // Explicit calendar-day distribution, never an inferred contractual schedule.
        public static List<PlannedDay> Distribute(Concept concept, DateTime start, DateTime end)
        {
            concept.Validate();
            var days = (end.Date - start.Date).Days + 1;
            if (start.Year < 1900 || end.Year > 2100 || days < 1 || days > 3660)
                throw new ArgumentException("Elige un período válido de hasta 10 años (1900–2100).");
            var quantity = decimal.Floor(concept.Quantity / days * 10000m) / 10000m;
            return Enumerable.Range(0, days).Select(i => new PlannedDay {
                ConceptId = concept.Id, Date = start.Date.AddDays(i),
                Quantity = i == days - 1 ? concept.Quantity - quantity * (days - 1) : quantity
            }).Where(d => d.Quantity > 0).ToList();
        }

        public static void ValidateDays(Concept concept, IList<PlannedDay> days, bool complete)
        {
            if (days.Count > 3660 || days.Any(d => d.ConceptId != concept.Id || d.Date.Year < 1900 || d.Date.Year > 2100 || d.Quantity <= 0 || decimal.Round(d.Quantity,4) != d.Quantity))
                throw new ArgumentException("Revisa fechas y cantidades del programa: usa cantidades positivas con hasta cuatro decimales.");
            if (days.Select(d => d.Date.Date).Distinct().Count() != days.Count)
                throw new ArgumentException("Hay fechas repetidas en el programa del concepto.");
            var total = days.Sum(d => d.Quantity);
            if (total > concept.Quantity || (complete && total != concept.Quantity))
                throw new ArgumentException("La cantidad programada de " + concept.Code + " debe completar exactamente " + concept.Quantity + " " + concept.Unit + ".");
        }

        public static void ValidateProgress(Concept concept, ProgressEntry entry, IEnumerable<ProgressEntry> history)
        {
            if (entry.ConceptId != concept.Id || entry.Date.Year < 1900 || entry.Date.Date > DateTime.Today)
                throw new ArgumentException("Selecciona una fecha válida, no posterior a hoy.");
            if (entry.Quantity <= 0 || entry.Quantity > concept.Quantity || decimal.Round(entry.Quantity,10) != entry.Quantity)
                throw new ArgumentException("El acumulado debe ser mayor a cero y no superar la cantidad contratada (100 %). Usa hasta diez decimales.");
            if ((entry.Note ?? "").Length > 500) throw new ArgumentException("La nota admite hasta 500 caracteres.");
            var items = history.ToList();
            if (items.Any(p => p.Date.Date == entry.Date.Date)) throw new ArgumentException("Ya existe un corte para este concepto en esa fecha.");
            var previous = items.Where(p => p.Date < entry.Date.Date).OrderByDescending(p => p.Date).FirstOrDefault();
            var next = items.Where(p => p.Date > entry.Date.Date).OrderBy(p => p.Date).FirstOrDefault();
            if ((previous != null && entry.Quantity <= previous.Quantity) || (next != null && entry.Quantity > next.Quantity))
                throw new ArgumentException("El acumulado debe aumentar respecto al corte anterior y no superar el siguiente.");
        }

        public static decimal ActualAt(IEnumerable<ProgressEntry> entries, DateTime cut)
            => entries.Where(p => p.Date.Date <= cut.Date).OrderByDescending(p => p.Date).FirstOrDefault()?.Quantity ?? 0;
    }
}
