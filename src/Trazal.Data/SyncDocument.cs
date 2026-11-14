using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using Trazal.Core;

namespace Trazal.Data
{
    [DataContract] public sealed class SyncWork
    {
        [DataMember(Name="id",Order=0)] public string Id {get;set;}
        [DataMember(Name="title",Order=1)] public string Title {get;set;}
        [DataMember(Name="type",Order=2)] public string Type {get;set;}
        [DataMember(Name="scope",Order=3)] public string Scope {get;set;}
        [DataMember(Name="vat",Order=4)] public string Vat {get;set;}
        [DataMember(Name="createdAt",Order=5)] public string CreatedAt {get;set;}
    }
    [DataContract] public sealed class SyncConcept
    {
        [DataMember(Name="id",Order=0)] public string Id {get;set;}
        [DataMember(Name="code",Order=1)] public string Code {get;set;}
        [DataMember(Name="description",Order=2)] public string Description {get;set;}
        [DataMember(Name="unit",Order=3)] public string Unit {get;set;}
        [DataMember(Name="quantity",Order=4)] public string Quantity {get;set;}
        [DataMember(Name="unitPrice",Order=5)] public string UnitPrice {get;set;}
    }
    [DataContract] public sealed class SyncDay
    {
        [DataMember(Name="conceptId",Order=0)] public string ConceptId {get;set;}
        [DataMember(Name="date",Order=1)] public string Date {get;set;}
        [DataMember(Name="quantity",Order=2)] public string Quantity {get;set;}
    }
    [DataContract] public sealed class SyncProgress
    {
        [DataMember(Name="id",Order=0)] public string Id {get;set;}
        [DataMember(Name="conceptId",Order=1)] public string ConceptId {get;set;}
        [DataMember(Name="date",Order=2)] public string Date {get;set;}
        [DataMember(Name="quantity",Order=3)] public string Quantity {get;set;}
        [DataMember(Name="note",Order=4)] public string Note {get;set;}
    }
    [DataContract] public sealed class SyncExpense
    {
        [DataMember(Name="id",Order=0)] public string Id {get;set;}
        [DataMember(Name="date",Order=1)] public string Date {get;set;}
        [DataMember(Name="category",Order=2)] public string Category {get;set;}
        [DataMember(Name="description",Order=3)] public string Description {get;set;}
        [DataMember(Name="amount",Order=4)] public string Amount {get;set;}
        [DataMember(Name="vat",Order=5)] public string Vat {get;set;}
    }
    [DataContract] public sealed class SyncDocument
    {
        public const int MaxBytes=512*1024;
        [DataMember(Name="schemaVersion",Order=0)] public int SchemaVersion {get;set;}=1;
        [DataMember(Name="work",Order=1)] public SyncWork Work {get;set;}
        [DataMember(Name="frozen",Order=2)] public bool Frozen {get;set;}
        [DataMember(Name="concepts",Order=3)] public List<SyncConcept> Concepts {get;set;}=new List<SyncConcept>();
        [DataMember(Name="plan",Order=4)] public List<SyncDay> Plan {get;set;}=new List<SyncDay>();
        [DataMember(Name="progress",Order=5)] public List<SyncProgress> Progress {get;set;}=new List<SyncProgress>();
        [DataMember(Name="expenses",Order=6)] public List<SyncExpense> Expenses {get;set;}=new List<SyncExpense>();
        internal static decimal Number(string value) { decimal n;if(value==null||!decimal.TryParse(value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out n))throw new ArgumentException("Decimal inválido en la obra remota.");return n; }
        internal static DateTime Day(string value)=>DateTime.ParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture);
        internal static string Numeric(decimal value)=>value.ToString("G29",CultureInfo.InvariantCulture);
        internal Work ToWork()=>new Work{Id=Work.Id,Title=Work.Title,Type=Work.Type,Scope=Work.Scope,Vat=Work.Vat==null?(decimal?)null:Number(Work.Vat),CreatedAt=DateTime.Parse(Work.CreatedAt,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)};
        internal List<Concept> ToConcepts()=>Concepts.Select(c=>new Concept{Id=c.Id,WorkId=Work.Id,Code=c.Code,Description=c.Description,Unit=c.Unit,Quantity=Number(c.Quantity),UnitPrice=Number(c.UnitPrice)}).ToList();
        internal List<PlannedDay> ToPlan()=>Plan.Select(p=>new PlannedDay{ConceptId=p.ConceptId,Date=Day(p.Date),Quantity=Number(p.Quantity)}).ToList();
        internal List<ProgressEntry> ToProgress()=>Progress.Select(p=>new ProgressEntry{Id=p.Id,ConceptId=p.ConceptId,Date=Day(p.Date),Quantity=Number(p.Quantity),Note=p.Note}).ToList();
        internal List<Expense> ToExpenses()=>Expenses.Select(e=>new Expense{Id=e.Id,WorkId=Work.Id,Date=Day(e.Date),Category=e.Category,Description=e.Description,Amount=Number(e.Amount),Vat=Number(e.Vat)}).ToList();
        public void Validate()
        {
            if(SchemaVersion!=1||Work==null||Concepts==null||Plan==null||Progress==null||Expenses==null)throw new ArgumentException("Documento remoto incompleto.");
            if(Concepts.Count>2000||Plan.Count>10000||Progress.Count>5000||Expenses.Count>5000)throw new ArgumentException("La obra excede los límites del piloto de sincronización.");
            var w=ToWork();w.Validate();ValidateId(w.Id);
            var cs=ToConcepts();var ds=ToPlan();var ps=ToProgress();var es=ToExpenses();
            var ids=new HashSet<string>();var codes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var c in cs){ValidateId(c.Id);c.Validate();if(!ids.Add(c.Id)||!codes.Add(c.Code.Trim()))throw new ArgumentException("Concepto duplicado.");}
            if(ds.Any(d=>!ids.Contains(d.ConceptId))||ps.Any(p=>!ids.Contains(p.ConceptId)))throw new ArgumentException("Registro sin concepto.");
            if(Frozen&&cs.Count==0)throw new ArgumentException("Línea base vacía.");
            if(!Frozen&&ps.Count>0)throw new ArgumentException("Avances sin línea base.");
            var planGroups=ds.ToLookup(d=>d.ConceptId);var progressGroups=ps.ToLookup(p=>p.ConceptId);
            foreach(var c in cs){Scheduling.ValidateDays(c,planGroups[c.Id].ToList(),Frozen);ProgressEntry previous=null;foreach(var p in progressGroups[c.Id].OrderBy(p=>p.Date)){ValidateId(p.Id);Scheduling.ValidateProgress(c,p,new ProgressEntry[0]);if(previous!=null&&(p.Date==previous.Date||p.Quantity<previous.Quantity))throw new ArgumentException("Avance remoto inválido.");previous=p;}}
            if(ps.Select(p=>p.Id).Distinct().Count()!=ps.Count||es.Select(e=>e.Id).Distinct().Count()!=es.Count)throw new ArgumentException("Registros remotos duplicados.");
            foreach(var e in es){ValidateId(e.Id);e.Validate();}
        }
        private static void ValidateId(string value) { if(value==null||value.Length!=32||!System.Text.RegularExpressions.Regex.IsMatch(value,"^[0-9a-f]{32}$"))throw new ArgumentException("Identificador remoto inválido."); }
        public string ToJson()
        {
            Concepts=Concepts.OrderBy(c=>c.Id,StringComparer.Ordinal).ToList();Plan=Plan.OrderBy(p=>p.ConceptId,StringComparer.Ordinal).ThenBy(p=>p.Date,StringComparer.Ordinal).ToList();Progress=Progress.OrderBy(p=>p.Id,StringComparer.Ordinal).ToList();Expenses=Expenses.OrderBy(e=>e.Id,StringComparer.Ordinal).ToList();
            return SyncJson.Write(this);
        }
    }
    public static class SyncJson
    {
        public static string Write<T>(T value){using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);return Encoding.UTF8.GetString(stream.ToArray());}}
        public static T Read<T>(string json){using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json))){return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}}
        public static string Hash(string json){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-","").ToLowerInvariant();}
    }
}
