using System;
using System.Globalization;
using System.Linq;
using Trazal.Core;

namespace Trazal.Desktop
{
    public sealed partial class ShellViewModel
    {
        public void SetStatus(string message)=>Status=message;
        public void OpenImport(Work work)=>Safe(()=>{if(store.IsFrozen(work.Id))throw new ArgumentException("El programa ya está fijado.");Page=new ImportViewModel(this,store,work);Location="Mi obra / Importar catálogo";});
        public void OpenProgram(Work work,bool actual=false,string selectedId=null,DateTime? cut=null)=>Safe(()=>{Page=new ProgramViewModel(this,store,work,actual,selectedId,cut);Location="Mi obra / "+(actual?"Avance real":"Programa");});
        private static DateTime ReadDate(Editor e,string key)
        {
            DateTime date;
            if(!DateTime.TryParseExact(e.Value(key),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))throw new ArgumentException("Selecciona una fecha válida.");
            return date;
        }
        private void ReloadProgram(ProgramViewModel view,string message)
        {Dialog=null;OpenProgram(view.Work,view.IsProgress,view.Selected?.Concept.Id,view.Cut);Status=message;}
        public void DistributePlan(ProgramViewModel view)
        {
            var c=view.Selected.Concept;
            var e=new Editor{Title="Distribuir concepto",Help="Reparte la cantidad contratada en días naturales, incluidos fines de semana. Reemplaza el borrador de este concepto. Después puedes ajustar cantidades por día. No calcula rendimientos ni días hábiles."};
            e.Add("start","Inicio *",(view.Days.FirstOrDefault()?.Date??DateTime.Today).ToString("yyyy-MM-dd"));
            e.Add("end","Término *",(view.Days.LastOrDefault()?.Date??DateTime.Today).ToString("yyyy-MM-dd"));
            Func<System.Collections.Generic.List<PlannedDay>> read=()=>Scheduling.Distribute(c,ReadDate(e,"start"),ReadDate(e,"end"));
            e.Configure(()=>read(),()=>{store.SavePlan(view.Work.Id,c.Id,read());ReloadProgram(view,"Distribución guardada como borrador.");},()=>Dialog=null,view.Days.Count>0);
            Dialog=e;
        }
        public void EditPlanDay(ProgramViewModel view,PlannedDay existing)
        {
            var c=view.Selected.Concept;
            var e=new Editor{Title=existing==null?"Agregar día":"Ajustar día",Help="Cantidad prevista para este día, no acumulada. Usa 0 al editar para quitar ese día. El programa debe completar la cantidad contratada antes de fijarse."};
            e.Add("date","Fecha *",(existing?.Date??DateTime.Today).ToString("yyyy-MM-dd"));
            e.Add("quantity","Cantidad del día ("+c.Unit+") *",existing?.Quantity.ToString("0.####",CultureInfo.InvariantCulture)??"",max:24,placeholder:"Ej. 12.50");
            Func<System.Collections.Generic.List<PlannedDay>> read=()=>{
                var date=ReadDate(e,"date");var quantity=Parse(e.Value("quantity"),"la cantidad");
                if(quantity<0 || (existing==null&&quantity==0))throw new ArgumentException("Indica una cantidad positiva.");
                var days=view.Days.Where(d=>existing==null||d.Date!=existing.Date).ToList();
                if(quantity>0)days.Add(new PlannedDay{ConceptId=c.Id,Date=date,Quantity=quantity});
                Scheduling.ValidateDays(c,days,false);return days;
            };
            e.Configure(()=>read(),()=>{store.SavePlan(view.Work.Id,c.Id,read());ReloadProgram(view,"Día actualizado en el borrador.");},()=>Dialog=null,existing!=null);Dialog=e;
        }
        public void ApproveProgram(ProgramViewModel view)
        {
            var e=new Editor{Title="Fijar programa",SaveLabel="Fijar línea base",Help="El catálogo, precios, cantidades y días programados quedarán protegidos. Los avances y gastos seguirán abiertos. Las penalizaciones se gestionan externamente; las fechas sirven como recordatorio."};
            e.Add("confirm","Confirmación *",choices:new[]{"Revisé el presupuesto y el programa; fijar línea base"},placeholder:"Confirma que terminaste la revisión");
            e.Configure(()=>{if(e.Value("confirm")=="")throw new ArgumentException("Confirma la revisión.");},()=>{store.FreezeProgram(view.Work.Id);ReloadProgram(view,"Programa fijado. Ya puedes registrar avances reales.");},()=>Dialog=null,false);Dialog=e;
        }
        public void RecordProgress(ProgramViewModel view)
        {
            var c=view.Selected.Concept;
            var e=new Editor{Title="Registrar avance",Help="Captura lo ejecutado acumulado hasta la fecha, no solo lo realizado ese día. Puede ser cantidad o porcentaje. Cada fecha admite un corte por concepto. El programa original no cambia."};
            e.Add("date","Fecha de medición *",(view.Cut??DateTime.Today).ToString("yyyy-MM-dd"));
            e.Add("mode","Capturar como *","Cantidad acumulada",choices:new[]{"Cantidad acumulada","Porcentaje acumulado"});
            e.Add("quantity","Cantidad acumulada ("+c.Unit+") *","","Contratado: "+c.Quantity.ToString("0.####")+" "+c.Unit+". Máximo: 100 %.",24,placeholder:"Ej. 30");
            e.Add("note","Observación (opcional)",max:500,placeholder:"Ej. Medición de la banqueta del tramo 1");
            e.Fields.Single(f=>f.Key=="mode").PropertyChanged+=(_,args)=>{if(args.PropertyName=="Value")e.Fields.Single(f=>f.Key=="quantity").Label=e.Value("mode")=="Porcentaje acumulado"?"Avance acumulado (%) *":"Cantidad acumulada ("+c.Unit+") *";};
            Func<ProgressEntry> read=()=>{
                var value=Parse(e.Value("quantity"),"el acumulado");
                if(e.Value("mode")=="Porcentaje acumulado")
                {if(value<=0||value>100||decimal.Round(value,4)!=value)throw new ArgumentException("El porcentaje debe ser mayor a 0 y como máximo 100, con hasta cuatro decimales.");value=c.Quantity*value/100m;}
                var p=new ProgressEntry{ConceptId=c.Id,Date=ReadDate(e,"date"),Quantity=value,Note=e.Value("note")};
                Scheduling.ValidateProgress(c,p,view.History);return p;
            };
            e.Configure(()=>read(),()=>{var p=read();store.AddProgress(view.Work.Id,p);ReloadProgram(view,p.Quantity==c.Quantity?"Concepto completado: 100 %. No admite cantidades superiores.":"Avance guardado sin modificar el programa.");},()=>Dialog=null,false);Dialog=e;
        }
    }
}
