using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Trazal.Core;
using Trazal.Data;

namespace Trazal.Desktop
{
    public static class PdfReport
    {
        public static void Write(WorkReport report,string path) => WorkReport.WriteAtomically(path,temp=>{
            using(var writer=new Writer(report)){writer.Create();writer.Document.Save(temp);}
        });
        private sealed class Writer : IDisposable
        {
            public PdfDocument Document { get; } = new PdfDocument();
            private readonly WorkReport report;
            private XGraphics g;
            private readonly XFont normal=new XFont("Segoe UI",10,XFontStyleEx.Regular);
            private readonly XFont small=new XFont("Segoe UI",9,XFontStyleEx.Regular);
            private readonly XFont heading=new XFont("Segoe UI",22,XFontStyleEx.Bold);
            private readonly XFont bold=new XFont("Segoe UI",11,XFontStyleEx.Bold);
            private readonly XBrush ink=new XSolidBrush(XColor.FromArgb(48,52,43));
            private readonly XBrush muted=new XSolidBrush(XColor.FromArgb(96,106,87));
            private readonly XBrush yellow=new XSolidBrush(XColor.FromArgb(248,223,71));
            private double width,height,y;
            public Writer(WorkReport report) {this.report=report;Document.Info.Title="Trazal - "+report.Work.Title;Document.Info.Author="Trazal";}
            private void Text(string text,XFont font,XBrush brush,double x,double top) => g.DrawString(text,font,brush,new XPoint(x,top+font.Size));
            private List<string> Wrap(string text,XFont font,double max)
            {
                var lines=new List<string>();string line="";
                foreach(var paragraph in (text??"").Replace("\r","").Split('\n'))
                {
                    foreach(var word in paragraph.Split(' '))
                    {
                        var next=line.Length==0?word:line+" "+word;
                        if(g.MeasureString(next,font).Width<=max) {line=next;continue;}
                        if(line.Length>0) {lines.Add(line);line="";}
                        foreach(var ch in word){if(g.MeasureString(line+ch,font).Width>max&&line.Length>0){lines.Add(line);line="";}line+=ch;}
                    }
                    lines.Add(line);line="";
                }
                return lines;
            }
            private void Paragraph(string text,XFont font,XBrush brush,double x,double top,double max)
            {foreach(var line in Wrap(text,font,max)){Text(line,font,brush,x,top);top+=font.Size+4;}}
            private void NewPage(string title)
            {
                g?.Dispose();var page=Document.AddPage();page.Size=PageSize.A4;page.Orientation=PageOrientation.Landscape;
                width=page.Width.Point-64;height=page.Height.Point;g=XGraphics.FromPdfPage(page);
                g.DrawRectangle(yellow,32,28,34,6);Text("TRAZAL / CONTROL DE OBRA",small,muted,77,23);
                Text(title,heading,ink,32,50);
                var caption=Wrap(report.Caption,small,width);double cy=82;foreach(var line in caption){Text(line,small,muted,32,cy);cy+=13;}
                y=cy+18;Text("Trazal · Reporte local · "+DateTime.Now.ToString("dd/MM/yyyy HH:mm"),small,muted,32,height-27);
                Text("Página "+Document.PageCount,small,muted,width-20,height-27);
            }
            public void Create()
            {
                NewPage("Curva S y desempeño");var p=report.Performance;
                var values=new[]{p.Planned,p.Earned,p.Spent};var labels=new[]{"Programa (PV)","Ejecutado (EV)","Gastos (AC)"};
                for(int i=0;i<3;i++){double x=32+i*(width/3);Text(labels[i],normal,muted,x,y);Text(Money(values[i]),heading,ink,x,y+19);}
                y+=60;Text("SPI: "+(report.Frozen?PerformanceViewModel.Ratio(p.Spi):"Sin línea base")+"     CPI orientativo: "+(report.Frozen?PerformanceViewModel.Ratio(p.Cpi):"Sin línea base")+"     Importes sin IVA",bold,ink,32,y);y+=28;
                Chart(32,y,width,205);y+=218;
                Paragraph(WorkReport.Method,small,muted,32,y,width);
                foreach(var table in report.Tables.Where(t=>t.Name!="Curva S"&&t.Name!="Programa diario")) Table(table);
            }
            private static string Money(decimal value)=>value.ToString("C2",CultureInfo.GetCultureInfo("es-MX"));
            private static string Display(object value)=>value==null?"—":value is DateTime?((DateTime)value).ToString("dd/MM/yyyy"):value is decimal?((decimal)value).ToString("#,##0.00##",CultureInfo.GetCultureInfo("es-MX")):Convert.ToString(value);
            private void Table(ReportTable table)
            {
                NewPage(table.Name);var widths=table.Widths.Select(w=>w/table.Widths.Sum()*width).ToArray();
                double headerHeight=Math.Max(38,table.Headers.Select((h,i)=>Wrap(h,small,widths[i]-14).Count).Max()*13+14);
                Action headers=()=>{g.DrawRectangle(yellow,32,y,width,headerHeight);double x=32;for(int i=0;i<widths.Length;i++){Paragraph(table.Headers[i],small,ink,x+7,y+5,widths[i]-14);x+=widths[i];}y+=headerHeight;};
                headers();int index=0;
                if(table.Rows.Count==0){Text("Sin registros para este corte.",normal,muted,40,y+18);return;}
                foreach(var row in table.Rows)
                {
                    var lines=row.Select((v,i)=>Wrap(Display(v),small,widths[i]-14)).ToArray();double rowHeight=Math.Max(32,lines.Max(l=>l.Count)*13+14);
                    if(y+rowHeight>height-46){NewPage(table.Name+" / continuación");headers();}
                    if(index++%2==0)g.DrawRectangle(new XSolidBrush(XColor.FromArgb(246,247,240)),32,y,width,rowHeight);
                    double x=32;for(int i=0;i<row.Length;i++){double ty=y+7;foreach(var line in lines[i]){Text(line,small,ink,x+7,ty);ty+=13;}x+=widths[i];}
                    y+=rowHeight;
                }
            }
            private void Chart(double left,double top,double w,double h)
            {
                var p=report.Performance;double x0=left+74,y0=top+12,pw=w-92,ph=h-54;var first=p.Points.First().Date;double span=Math.Max(1,(p.Points.Last().Date-first).TotalDays);
                decimal max=Math.Max(1,p.Points.Max(v=>Math.Max(v.Planned,Math.Max(v.Earned??0,v.Spent??0)))*1.1m);
                Func<DateTime,double> x=d=>x0+(d-first).TotalDays/span*pw;Func<decimal,double> yy=v=>y0+ph*(1-(double)(v/max));
                for(int i=0;i<=4;i++){var v=max*i/4;g.DrawLine(new XPen(XColors.LightGray,0.5),x0,yy(v),x0+pw,yy(v));Text(v>=1000000?(v/1000000).ToString("0.##")+" M":v>=1000?(v/1000).ToString("0.#")+" mil":v.ToString("0.##"),small,muted,left,yy(v)-6);}
                for(int i=0;i<=3;i++){var d=first.AddDays(span*i/3);Text(d.ToString("dd/MM/yy"),small,muted,x(d)-20,y0+ph+8);}
                var colors=new[]{XColor.FromArgb(65,151,201),XColor.FromArgb(125,132,138),XColor.FromArgb(222,99,84)};
                var getters=new Func<CurvePoint,decimal?>[]{v=>v.Planned,v=>v.Earned,v=>v.Spent};
                for(int j=0;j<3;j++)
                {
                    var pen=new XPen(colors[j],1.8){DashStyle=j==0?XDashStyle.Solid:j==1?XDashStyle.Dash:XDashStyle.Dot};CurvePoint previous=null;
                    foreach(var row in p.Points){var v=getters[j](row);if(!v.HasValue)break;if(previous!=null){double prevY=yy(getters[j](previous).Value);g.DrawLine(pen,x(previous.Date),prevY,x(row.Date),prevY);g.DrawLine(pen,x(row.Date),prevY,x(row.Date),yy(v.Value));}previous=row;}
                    g.DrawLine(pen,left+j*200,top+h-8,left+j*200+23,top+h-8);Text(new[]{"Programa fijo","Ejecutado acumulado","Gastos acumulados"}[j],small,muted,left+j*200+30,top+h-15);
                }
                g.DrawLine(new XPen(XColors.Gray,0.8){DashStyle=XDashStyle.Dash},x(p.Cut),y0,x(p.Cut),y0+ph);
            }
            public void Dispose(){g?.Dispose();Document.Dispose();}
        }
    }
}
