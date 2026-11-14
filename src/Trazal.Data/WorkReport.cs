using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Trazal.Core;

namespace Trazal.Data
{
    public sealed class ReportTable
    {
        public string Name { get; set; }
        public string[] Headers { get; set; }
        public double[] Widths { get; set; }
        public List<object[]> Rows { get; } = new List<object[]>();
    }
    public sealed class WorkReport
    {
        public Work Work { get; private set; }
        public bool Frozen { get; private set; }
        public PerformanceSnapshot Performance { get; private set; }
        public List<ReportTable> Tables { get; } = new List<ReportTable>();
        public const string Method = "MXN sin IVA. Ejecutado = última cantidad acumulada × importe presupuestado / cantidad contratada. SPI = ejecutado / programa; CPI = ejecutado / gastos. Sin denominador se muestra No disponible. El CPI es orientativo: los precios pueden incluir utilidad y las compras pueden no haberse consumido. No representa utilidad contable.";
        public string Caption => Work.Title + " | Corte: " + Performance.Cut.ToString("dd/MM/yyyy") + " | " + (Frozen ? "Programa fijado" : "BORRADOR") + " | MXN";
        public static WorkReport Read(LocalStore store, string workId, DateTime cut)
        {
            var report = new WorkReport { Work = store.GetWorks().Single(w => w.Id == workId), Frozen = store.IsFrozen(workId) };
            var concepts = store.GetConcepts(workId); var plan = store.GetPlan(workId);
            var progress = store.GetProgress(workId).Where(entry => entry.Date <= cut.Date).ToList();
            var expenses = store.GetExpenses(workId).Where(e => e.Date <= cut.Date).OrderBy(e => e.Date).ToList();
            var map = concepts.ToDictionary(c => c.Id);
            report.Performance = Trazal.Core.Performance.Calculate(concepts, plan, progress, expenses, cut);
            var p = report.Performance; var budget = Budget.Calculate(concepts, report.Work.Vat);
            var summary = report.Add("Resumen", new[] { "Indicador", "Valor" }, new double[] { 36, 92 });
            summary.Rows.AddRange(new[] {
                new object[] {"Obra",report.Work.Title}, new object[]{"Tipo",report.Work.Type},new object[]{"Alcance",report.Work.Scope},
                new object[]{"Corte",cut.Date},new object[]{"Programa",report.Frozen?"Fijado":"Borrador"},
                new object[]{"Presupuesto sin IVA",budget.Subtotal},new object[]{"IVA presupuesto",(object)budget.Tax??"Por definir"},new object[]{"Total presupuesto",(object)budget.Total??"IVA pendiente"},
                new object[]{"Programado al corte (PV)",p.Planned},new object[]{"Ejecutado al corte (EV)",p.Earned},new object[]{"Gastos al corte (AC)",p.Spent},
                new object[]{"IVA de gastos",expenses.Sum(e=>e.Tax)},new object[]{"Gastos con IVA",expenses.Sum(e=>e.Total)},
                new object[]{"SPI",report.Frozen?(object)p.Spi??"No disponible":"Sin línea base"},new object[]{"CPI orientativo",report.Frozen?(object)p.Cpi??"No disponible":"Sin línea base"},
                new object[]{"Criterio",Method},new object[]{"Alcance del reporte","Presupuesto y programa completos. Avances y gastos hasta el corte. Los importes son una instantánea; editar este Excel no modifica Trazal."}
            });
            var b = report.Add("Presupuesto",new[]{"Clave","Trabajo / descripción","Unidad","Cantidad","P. unitario sin IVA","Importe sin IVA"},new double[]{16,58,12,18,22,22});
            foreach(var c in concepts) b.Rows.Add(new object[]{c.Code,c.Description,c.Unit,c.Quantity,c.UnitPrice,c.Amount});
            var s = report.Add("Programa",new[]{"Clave","Trabajo / descripción","Inicio","Término","Cantidad programada","Unidad"},new double[]{16,58,18,18,24,12});
            var planByConcept=plan.ToLookup(d=>d.ConceptId);
            foreach(var c in concepts) { var ds=planByConcept[c.Id].OrderBy(d=>d.Date).ToList(); s.Rows.Add(new object[]{c.Code,c.Description,(object)ds.FirstOrDefault()?.Date??"Pendiente",(object)ds.LastOrDefault()?.Date??"Pendiente",ds.Sum(d=>d.Quantity),c.Unit}); }
            var a = report.Add("Avances",new[]{"Fecha","Clave","Trabajo / descripción","Acumulado","Unidad","Avance (%)","Observación"},new double[]{18,16,48,18,12,18,45});
            foreach(var entry in progress.OrderBy(e=>e.Date)) { var c=map[entry.ConceptId]; a.Rows.Add(new object[]{entry.Date,c.Code,c.Description,entry.Quantity,c.Unit,entry.Quantity/c.Quantity*100,entry.Note}); }
            var eTable = report.Add("Gastos",new[]{"Fecha","Categoría","Descripción","Sin IVA","IVA (%)","IVA (MXN)","Total"},new double[]{18,32,55,22,12,22,22});
            foreach(var e in expenses) eTable.Rows.Add(new object[]{e.Date,e.Category,e.Description,e.Amount,e.Vat,e.Tax,e.Total});
            var groups = report.Add("Por categoría",new[]{"Categoría","Sin IVA","IVA (MXN)","Total"},new double[]{55,24,24,24});
            foreach(var group in expenses.GroupBy(e=>e.Category)) groups.Rows.Add(new object[]{group.Key,group.Sum(e=>e.Amount),group.Sum(e=>e.Tax),group.Sum(e=>e.Total)});
            var curve = report.Add("Curva S",new[]{"Fecha","Programa fijo (PV)","Ejecutado (EV)","Gastos (AC)"},new double[]{18,28,28,28});
            foreach(var point in p.Points) curve.Rows.Add(new object[]{point.Date,point.Planned,point.Earned,point.Spent});
            var daily=report.Add("Programa diario",new[]{"Fecha","Clave","Descripción","Cantidad del día","Unidad"},new double[]{18,16,58,24,12});
            foreach(var d in plan.OrderBy(d=>d.Date).ThenBy(d=>map[d.ConceptId].Code)) daily.Rows.Add(new object[]{d.Date,map[d.ConceptId].Code,map[d.ConceptId].Description,d.Quantity,map[d.ConceptId].Unit});
            return report;
        }
        private ReportTable Add(string name,string[] headers,double[] widths) { var table=new ReportTable{Name=name,Headers=headers,Widths=widths};Tables.Add(table);return table; }
        public static void WriteAtomically(string path, Action<string> write)
        {
            path=Path.GetFullPath(path);var temp=Path.Combine(Path.GetDirectoryName(path),Guid.NewGuid().ToString("N")+Path.GetExtension(path));
            try { write(temp);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path); }
            finally { if(File.Exists(temp))File.Delete(temp); }
        }
    }
}
