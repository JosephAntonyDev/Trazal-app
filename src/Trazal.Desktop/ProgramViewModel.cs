using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Trazal.Core;
using Trazal.Data;

namespace Trazal.Desktop
{
    public sealed class ProgramRow
    {
        public Concept Concept{get;set;}
        public string Code=>Concept.Code;
        public string Description=>Concept.Description;
        public string Unit=>Concept.Unit;
        public decimal Quantity=>Concept.Quantity;
        public decimal Scheduled{get;set;}
        public decimal Planned{get;set;}
        public decimal Actual{get;set;}
        public decimal Percent=>Actual/Quantity*100;
        public decimal PlannedPercent=>Planned/Quantity*100;
        public string State=>Actual==Quantity?"Completado · 100 %":Actual>Planned?"Adelantado":Actual<Planned?"Atrasado":"Conforme al programa";
        public string Coverage=>Scheduled==Quantity?"Completo":Scheduled>Quantity?"Excede cantidad":"Pendiente de distribuir";
        public override string ToString()=>Code+" · "+Description;
    }
    public sealed class ProgramViewModel : Observable
    {
        private readonly ShellViewModel shell;
        private readonly LocalStore store;
        public Work Work{get;}
        public bool IsProgress{get;}
        public bool IsPlan=>!IsProgress;
        public bool Frozen{get;}
        public bool Editable=>!Frozen && IsPlan;
        public string Title=>IsProgress?"Avance real":"Programa de obra";
        public string State=>Frozen?"Programa fijado · línea base protegida":"Borrador · completa la distribución de cada concepto";
        private DateTime? cut=DateTime.Today;
        public DateTime? Cut{get=>cut;set{if(Set(ref cut,value))Refresh();}}
        public List<ProgramRow> Rows{get;private set;}
        private ProgramRow selected;
        public ProgramRow Selected{get=>selected;set{if(Set(ref selected,value)){Notify(nameof(Days));Notify(nameof(History));Notify(nameof(Detail));CommandManager.InvalidateRequerySuggested();}}}
        private readonly List<PlannedDay> plan;
        private readonly List<ProgressEntry> progress;
        public List<PlannedDay> Days=>plan.Where(d=>d.ConceptId==Selected?.Concept.Id).OrderBy(d=>d.Date).ToList();
        public List<ProgressEntry> History=>progress.Where(p=>p.ConceptId==Selected?.Concept.Id).OrderByDescending(p=>p.Date).ToList();
        public string Detail=>Selected==null?"Selecciona un concepto para ver su detalle.":Selected.Code+" · "+Selected.Quantity.ToString("0.####")+" "+Selected.Unit+" contratados";
        public string Summary=>Rows==null?"":Rows.Count+(Rows.Count==1?" concepto · ":" conceptos · ")+Rows.Count(r=>r.Scheduled==r.Quantity)+" con programa completo";
        public string Totals
        {
            get
            {
                if(Rows==null || !Cut.HasValue)return "Selecciona la fecha de corte.";
                decimal planned=Rows.Sum(r=>Budget.Round(r.Concept.Amount*r.Planned/r.Quantity));
                decimal actual=Rows.Sum(r=>Budget.Round(r.Concept.Amount*r.Actual/r.Quantity));
                return "Al corte, sin IVA · Programado: "+ShellViewModel.Money(planned)+" · Ejecutado a precio de presupuesto: "+ShellViewModel.Money(actual);
            }
        }
        public ICommand Back{get;}
        public ICommand Switch{get;}
        public string SwitchLabel=>IsProgress?"Ver programa":"Ver avance real";
        public ICommand Distribute{get;}
        public ICommand Day{get;}
        public ICommand EditDay{get;}
        public ICommand Freeze{get;}
        public ICommand Record{get;}
        public ProgramViewModel(ShellViewModel shell,LocalStore store,Work work,bool actual,string selectedId=null,DateTime? cutDate=null)
        {
            this.shell=shell;this.store=store;Work=work;IsProgress=actual;Frozen=store.IsFrozen(work.Id);
            plan=store.GetPlan(work.Id);progress=store.GetProgress(work.Id);cut=cutDate??DateTime.Today;
            Back=new ActionCommand(_=>shell.OpenWork(work));Switch=new ActionCommand(_=>shell.OpenProgram(work,!IsProgress,Selected?.Concept.Id,Cut));
            Distribute=new ActionCommand(_=>shell.DistributePlan(this),_=>Editable&&Selected!=null);
            Day=new ActionCommand(_=>shell.EditPlanDay(this,null),_=>Editable&&Selected!=null);
            EditDay=new ActionCommand(d=>shell.EditPlanDay(this,(PlannedDay)d),_=>Editable&&Selected!=null);
            Freeze=new ActionCommand(_=>shell.ApproveProgram(this),_=>!Frozen&&Rows.Count>0&&Rows.All(r=>r.Scheduled==r.Quantity));
            Record=new ActionCommand(_=>shell.RecordProgress(this),_=>Frozen&&Selected!=null&&Cut.HasValue&&Selected.Actual<Selected.Quantity);
            Refresh(); if(selectedId!=null)Selected=Rows.FirstOrDefault(r=>r.Concept.Id==selectedId)??Rows.FirstOrDefault();
        }
        private void Refresh()
        {
            var id=Selected?.Concept.Id;
            Rows=store.GetConcepts(Work.Id).Select(c=>new ProgramRow{Concept=c,Scheduled=plan.Where(d=>d.ConceptId==c.Id).Sum(d=>d.Quantity),Planned=plan.Where(d=>d.ConceptId==c.Id&&Cut.HasValue&&d.Date<=Cut.Value.Date).Sum(d=>d.Quantity),Actual=Cut.HasValue?Scheduling.ActualAt(progress.Where(p=>p.ConceptId==c.Id),Cut.Value):0}).ToList();
            Selected=Rows.FirstOrDefault(r=>r.Concept.Id==id)??Rows.FirstOrDefault();
            Notify(nameof(Rows));Notify(nameof(Summary));Notify(nameof(Totals));
        }
    }
}
