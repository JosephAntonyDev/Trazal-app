using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using Trazal.Core;

namespace Trazal.Data
{
    public sealed class CatalogSheet
    {
        public string Name { get; set; }
        public List<string[]> Rows { get; } = new List<string[]>();
        public int Width { get; set; }
        public string Cell(int row,int column) => row>=0 && row<Rows.Count && column>=0 && column<Rows[row].Length ? Rows[row][column] : "";
        public override string ToString() => Name;
    }
    public static class ExcelCatalog
    {
        public static List<CatalogSheet> Read(string path)
        {
            if(new FileInfo(path).Length>20*1024*1024) throw new ArgumentException("El archivo supera 20 MB. Separa el catálogo en un archivo más pequeño.");
            var result=new List<CatalogSheet>(); var cells=0;
            using(var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            using(var reader=ExcelReaderFactory.CreateReader(stream))
            {
                do
                {
                    if(result.Count>=50 || reader.FieldCount>256 || reader.RowCount>10000) throw new ArgumentException("Esta importación admite 50 hojas, 256 columnas y 10 000 filas por hoja.");
                    var sheet=new CatalogSheet{Name=reader.Name,Width=reader.FieldCount};
                    while(reader.Read())
                    {
                        cells+=reader.FieldCount;
                        if(cells>1000000) throw new ArgumentException("El libro es demasiado grande para esta vista previa. Exporta solo la hoja del catálogo.");
                        var row=new string[reader.FieldCount];
                        for(int col=0;col<row.Length;col++)
                        {
                            var value=reader.GetValue(col);
                            var mask=reader.GetNumberFormatString(col)??"";
                            // Preserve numeric identifiers whose display explicitly pads leading zeros.
                            row[col]=value is double && Regex.IsMatch(mask,"^0{2,40}$") ? ((double)value).ToString(mask,CultureInfo.InvariantCulture) : Convert.ToString(value,CultureInfo.InvariantCulture).Trim();
                        }
                        sheet.Rows.Add(row);
                    }
                    result.Add(sheet);
                } while(reader.NextResult());
            }
            return result;
        }
        public static string Normalize(string value)
        {
            var s=new string(value.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
            return Regex.Replace(s,"[^A-Z0-9]","");
        }
        public static decimal Number(string value)
        {
            var s=value.Trim().Replace("$","").Trim();
            if(!Regex.IsMatch(s,@"^[+-]?(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?$"))
                throw new ArgumentException("Número inválido: usa punto decimal (ej. 1250.50).");
            decimal number;
            if(!decimal.TryParse(s,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint|NumberStyles.AllowThousands,CultureInfo.InvariantCulture,out number)) throw new ArgumentException("Número fuera de rango.");
            return number;
        }
        public static Concept Parse(CatalogSheet sheet,int row,int[] columns,bool priceNextRow,string workId)
        {
            if(columns.Length!=5 || columns.Any(index=>index<0||index>=sheet.Width) || columns.Distinct().Count()!=5)
                throw new ArgumentException("Asigna cinco columnas distintas.");
            var c=new Concept{WorkId=workId,Code=sheet.Cell(row,columns[0]),Description=sheet.Cell(row,columns[1]),Unit=sheet.Cell(row,columns[2]),Quantity=Number(sheet.Cell(row,columns[3])),UnitPrice=Number(sheet.Cell(row+(priceNextRow?1:0),columns[4]))};
            c.Validate(); return c;
        }
    }
}
