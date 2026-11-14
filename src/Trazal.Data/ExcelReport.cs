using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;

namespace Trazal.Data
{
    // Small, typed Open XML snapshot. Inline strings never become executable spreadsheet formulas.
    public static class ExcelReport
    {
        private const string Ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string Rels="http://schemas.openxmlformats.org/package/2006/relationships";
        private const string Office="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        public static void Write(WorkReport report,string path) => WorkReport.WriteAtomically(path,temp=>{
            using(var file=File.Create(temp)) using(var zip=new ZipArchive(file,ZipArchiveMode.Create))
            {
                Part(zip,"[Content_Types].xml",w=>{w.WriteStartElement("Types","http://schemas.openxmlformats.org/package/2006/content-types");Empty(w,"Default","Extension","rels","ContentType","application/vnd.openxmlformats-package.relationships+xml");Empty(w,"Default","Extension","xml","ContentType","application/xml");Empty(w,"Override","PartName","/xl/workbook.xml","ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");Empty(w,"Override","PartName","/xl/styles.xml","ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");for(int i=1;i<=report.Tables.Count;i++)Empty(w,"Override","PartName","/xl/worksheets/sheet"+i+".xml","ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");w.WriteEndElement();});
                Part(zip,"_rels/.rels",w=>{w.WriteStartElement("Relationships",Rels);Empty(w,"Relationship","Id","rId1","Type",Office+"/officeDocument","Target","xl/workbook.xml");w.WriteEndElement();});
                Part(zip,"xl/_rels/workbook.xml.rels",w=>{w.WriteStartElement("Relationships",Rels);Empty(w,"Relationship","Id","style","Type",Office+"/styles","Target","styles.xml");for(int i=1;i<=report.Tables.Count;i++)Empty(w,"Relationship","Id","rId"+i,"Type",Office+"/worksheet","Target","worksheets/sheet"+i+".xml");w.WriteEndElement();});
                Part(zip,"xl/workbook.xml",w=>{w.WriteStartElement("workbook",Ns);w.WriteStartElement("sheets");for(int i=0;i<report.Tables.Count;i++){w.WriteStartElement("sheet");w.WriteAttributeString("name",report.Tables[i].Name);w.WriteAttributeString("sheetId",(i+1).ToString());w.WriteAttributeString("r","id",Office,"rId"+(i+1));w.WriteEndElement();}w.WriteEndElement();w.WriteEndElement();});
                Part(zip,"xl/styles.xml",Styles);
                for(int i=0;i<report.Tables.Count;i++) { var table=report.Tables[i];Part(zip,"xl/worksheets/sheet"+(i+1)+".xml",w=>Sheet(w,report,table)); }
            }
        });
        private static void Part(ZipArchive zip,string name,Action<XmlWriter> body) { using(var stream=zip.CreateEntry(name).Open())using(var w=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new System.Text.UTF8Encoding(false)})){w.WriteStartDocument();body(w);w.WriteEndDocument();} }
        private static void Empty(XmlWriter w,string tag,params string[] attributes) { w.WriteStartElement(tag);for(int i=0;i<attributes.Length;i+=2)w.WriteAttributeString(attributes[i],attributes[i+1]);w.WriteEndElement(); }
        private static void Styles(XmlWriter w)
        {
            w.WriteStartElement("styleSheet",Ns);
            w.WriteStartElement("numFmts");w.WriteAttributeString("count","2");Empty(w,"numFmt","numFmtId","164","formatCode","#,##0.00##;[Red](#,##0.00##);0.00");Empty(w,"numFmt","numFmtId","165","formatCode","dd/mm/yyyy");w.WriteEndElement();
            w.WriteStartElement("fonts");w.WriteAttributeString("count","2");for(int i=0;i<2;i++){w.WriteStartElement("font");Empty(w,"sz","val",i==0?"11":"12");Empty(w,"name","val","Calibri");Empty(w,"color","rgb","FF30342B");if(i==1)Empty(w,"b");w.WriteEndElement();}w.WriteEndElement();
            w.WriteStartElement("fills");w.WriteAttributeString("count","3");foreach(var pattern in new[]{"none","gray125","solid"}){w.WriteStartElement("fill");w.WriteStartElement("patternFill");w.WriteAttributeString("patternType",pattern);if(pattern=="solid")Empty(w,"fgColor","rgb","FFF8DF47");w.WriteEndElement();w.WriteEndElement();}w.WriteEndElement();
            w.WriteStartElement("borders");w.WriteAttributeString("count","1");Empty(w,"border");w.WriteEndElement();
            w.WriteStartElement("cellStyleXfs");w.WriteAttributeString("count","1");Empty(w,"xf","numFmtId","0","fontId","0","fillId","0","borderId","0");w.WriteEndElement();
            w.WriteStartElement("cellXfs");w.WriteAttributeString("count","4");for(int i=0;i<4;i++){w.WriteStartElement("xf");w.WriteAttributeString("numFmtId",i==2?"164":i==3?"165":"0");w.WriteAttributeString("fontId",i==1?"1":"0");w.WriteAttributeString("fillId",i==1?"2":"0");w.WriteAttributeString("borderId","0");w.WriteAttributeString("xfId","0");w.WriteAttributeString("applyAlignment","1");if(i>=2)w.WriteAttributeString("applyNumberFormat","1");Empty(w,"alignment","vertical","center","wrapText","1");w.WriteEndElement();}w.WriteEndElement();
            w.WriteStartElement("cellStyles");w.WriteAttributeString("count","1");Empty(w,"cellStyle","name","Normal","xfId","0","builtinId","0");w.WriteEndElement();w.WriteEndElement();
        }
        private static string Column(int i) { string result="";do{result=(char)('A'+i%26)+result;i=i/26-1;}while(i>=0);return result; }
        private static void Cell(XmlWriter w,int column,int row,object value,int style=-1)
        {
            w.WriteStartElement("c");w.WriteAttributeString("r",Column(column)+row);bool number=value is decimal||value is int||value is DateTime;
            w.WriteAttributeString("s",(style>=0?style:value is DateTime?3:number?2:0).ToString());
            if(number)w.WriteElementString("v",value is DateTime?((DateTime)value).ToOADate().ToString(CultureInfo.InvariantCulture):Convert.ToString(value,CultureInfo.InvariantCulture));
            else{w.WriteAttributeString("t","inlineStr");w.WriteStartElement("is");w.WriteStartElement("t");w.WriteAttributeString("xml","space",null,"preserve");w.WriteString(Convert.ToString(value)??"");w.WriteEndElement();w.WriteEndElement();}w.WriteEndElement();
        }
        private static void Sheet(XmlWriter w,WorkReport report,ReportTable table)
        {
            w.WriteStartElement("worksheet",Ns);w.WriteStartElement("sheetViews");w.WriteStartElement("sheetView");w.WriteAttributeString("workbookViewId","0");w.WriteAttributeString("showGridLines","0");Empty(w,"pane","ySplit","4","topLeftCell","A5","activePane","bottomLeft","state","frozen");w.WriteEndElement();w.WriteEndElement();
            w.WriteStartElement("cols");for(int i=0;i<table.Widths.Length;i++)Empty(w,"col","min",(i+1).ToString(),"max",(i+1).ToString(),"width",table.Widths[i].ToString(CultureInfo.InvariantCulture),"customWidth","1");w.WriteEndElement();
            w.WriteStartElement("sheetData");w.WriteStartElement("row");w.WriteAttributeString("r","1");w.WriteAttributeString("ht","30");w.WriteAttributeString("customHeight","1");Cell(w,0,1,"TRAZAL / "+table.Name,1);w.WriteEndElement();
            w.WriteStartElement("row");w.WriteAttributeString("r","2");w.WriteAttributeString("ht","42");w.WriteAttributeString("customHeight","1");Cell(w,0,2,report.Caption);w.WriteEndElement();
            w.WriteStartElement("row");w.WriteAttributeString("r","4");w.WriteAttributeString("ht","34");w.WriteAttributeString("customHeight","1");for(int i=0;i<table.Headers.Length;i++)Cell(w,i,4,table.Headers[i],1);w.WriteEndElement();
            int row=5;foreach(var values in table.Rows)
            {
                w.WriteStartElement("row");w.WriteAttributeString("r",row.ToString());var lines=values.Select((v,i)=>Math.Ceiling((Convert.ToString(v)??"").Length/Math.Max(8,table.Widths[i]-3))).DefaultIfEmpty(1).Max();w.WriteAttributeString("ht",Math.Min(409,Math.Max(30,lines*16)).ToString(CultureInfo.InvariantCulture));w.WriteAttributeString("customHeight","1");for(int i=0;i<values.Length;i++)Cell(w,i,row,values[i]);w.WriteEndElement();row++;
            }
            w.WriteEndElement();if(table.Rows.Count>0)Empty(w,"autoFilter","ref","A4:"+Column(table.Headers.Length-1)+(row-1));
            w.WriteStartElement("mergeCells");w.WriteAttributeString("count","2");Empty(w,"mergeCell","ref","A1:"+Column(table.Headers.Length-1)+"1");Empty(w,"mergeCell","ref","A2:"+Column(table.Headers.Length-1)+"2");w.WriteEndElement();
            Empty(w,"pageMargins","left","0.3","right","0.3","top","0.5","bottom","0.5","header","0.2","footer","0.2");Empty(w,"pageSetup","orientation","landscape","paperSize","9");w.WriteEndElement();
        }
    }
}
