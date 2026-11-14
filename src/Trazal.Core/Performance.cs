using System;
using System.Collections.Generic;
using System.Linq;

namespace Trazal.Core
{
    public sealed class CurvePoint
    {
        public DateTime Date { get; set; }
        public decimal Planned { get; set; }
        public decimal? Earned { get; set; }
        public decimal? Spent { get; set; }
    }
    public sealed class PerformanceSnapshot
    {
        public DateTime Cut { get; set; }
        public decimal Budget { get; set; }
        public decimal Planned { get; set; }
        public decimal Earned { get; set; }
        public decimal Spent { get; set; }
        public decimal? Spi => Planned == 0 ? (decimal?)null : Earned / Planned;
        public decimal? Cpi => Spent == 0 ? (decimal?)null : Earned / Spent;
        public decimal? Percent => Budget == 0 ? (decimal?)null : Earned / Budget * 100;
        public List<CurvePoint> Points { get; set; }
    }
    public static class Performance
    {
        // Event sweep: cumulative quantities are valued once per concept, avoiding daily money-rounding drift.
        public static PerformanceSnapshot Calculate(IList<Concept> concepts, IList<PlannedDay> plan,
            IList<ProgressEntry> progress, IList<Expense> expenses, DateTime cut)
        {
            cut = cut.Date;
            var catalog = concepts.ToDictionary(c => c.Id);
            var planned = concepts.ToDictionary(c => c.Id, _ => 0m);
            var actual = concepts.ToDictionary(c => c.Id, _ => 0m);
            var days = plan.ToLookup(p => p.Date.Date);
            var measurements = progress.Where(p => p.Date.Date <= cut).ToLookup(p => p.Date.Date);
            var costs = expenses.Where(e => e.Date.Date <= cut).ToLookup(e => e.Date.Date);
            var dates = new SortedSet<DateTime>(days.Select(g => g.Key).Concat(measurements.Select(g => g.Key)).Concat(costs.Select(g => g.Key))) { cut };
            if (dates.Min > DateTime.MinValue) dates.Add(dates.Min.AddDays(-1));
            decimal pv = 0, ev = 0, ac = 0;
            var result = new PerformanceSnapshot { Cut = cut, Budget = concepts.Sum(c => c.Amount), Points = new List<CurvePoint>() };
            foreach (var date in dates)
            {
                foreach (var day in days[date])
                {
                    var c = catalog[day.ConceptId];
                    pv -= Budget.Round(c.Amount * planned[c.Id] / c.Quantity);
                    planned[c.Id] += day.Quantity;
                    pv += Budget.Round(c.Amount * planned[c.Id] / c.Quantity);
                }
                foreach (var entry in measurements[date])
                {
                    var c = catalog[entry.ConceptId];
                    ev -= Budget.Round(c.Amount * actual[c.Id] / c.Quantity);
                    actual[c.Id] = entry.Quantity;
                    ev += Budget.Round(c.Amount * actual[c.Id] / c.Quantity);
                }
                ac += costs[date].Sum(e => e.Amount);
                result.Points.Add(new CurvePoint { Date = date, Planned = pv, Earned = date <= cut ? ev : (decimal?)null, Spent = date <= cut ? ac : (decimal?)null });
                if (date == cut) { result.Planned = pv; result.Earned = ev; result.Spent = ac; }
            }
            return result;
        }
    }
}
