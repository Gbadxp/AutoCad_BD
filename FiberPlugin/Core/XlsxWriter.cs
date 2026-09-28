using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace FiberPlugin.Core
{
    /// <summary>
    /// Gera planilhas Excel (.xlsx) com várias abas, sem bibliotecas externas: o .xlsx é um ZIP com XML.
    /// Suporta título, cabeçalho em negrito com fundo, números com 2 casas e largura de colunas.
    /// </summary>
    public sealed class XlsxWriter
    {
        public sealed class Sheet
        {
            internal Sheet(string name) { Name = name; }

            public string Name { get; }
            internal List<(object?[] Cells, int Style)> Rows { get; } = new List<(object?[], int)>();
            internal double[]? Widths { get; private set; }

            public Sheet Title(string text) { Rows.Add((new object?[] { text }, StyleTitle)); return this; }
            public Sheet Header(params string[] cells) { Rows.Add((cells.Cast<object?>().ToArray(), StyleHeader)); return this; }
            public Sheet Row(params object?[] cells) { Rows.Add((cells, StyleNormal)); return this; }
            public Sheet Blank() { Rows.Add((new object?[0], StyleNormal)); return this; }
            public Sheet ColumnWidths(params double[] widths) { Widths = widths; return this; }
        }

        // Índices em styles.xml
        private const int StyleNormal = 0;
        private const int StyleHeader = 1;
        private const int StyleDecimal = 2;
        private const int StyleTitle = 3;
        private const int StyleInteger = 4;

        private readonly List<Sheet> _sheets = new List<Sheet>();

        public Sheet AddSheet(string name)
        {
            var sheet = new Sheet(name.Length > 31 ? name.Substring(0, 31) : name);
            _sheets.Add(sheet);
            return sheet;
        }

        public void Save(string path)
        {
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml", ContentTypes());
                Add(zip, "_rels/.rels",
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                Add(zip, "xl/workbook.xml", Workbook());
                Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
                Add(zip, "xl/styles.xml", Styles());
                for (int i = 0; i < _sheets.Count; i++)
                {
                    Add(zip, $"xl/worksheets/sheet{i + 1}.xml", SheetXml(_sheets[i]));
                }
            }
        }

        private static void Add(ZipArchive zip, string name, string xml)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
                writer.Write(xml);
            }
        }

        private string ContentTypes()
        {
            var sb = new StringBuilder("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (int i = 0; i < _sheets.Count; i++)
            {
                sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }
            return sb.Append("</Types>").ToString();
        }

        private string Workbook()
        {
            var sb = new StringBuilder("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                                       "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            for (int i = 0; i < _sheets.Count; i++)
            {
                sb.Append($"<sheet name=\"{Escape(_sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
            }
            return sb.Append("</sheets></workbook>").ToString();
        }

        private string WorkbookRels()
        {
            var sb = new StringBuilder("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < _sheets.Count; i++)
            {
                sb.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
            }
            sb.Append($"<Relationship Id=\"rId{_sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            return sb.Append("</Relationships>").ToString();
        }

        private static string Styles()
        {
            return "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                   "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"0.00\"/></numFmts>" +
                   "<fonts count=\"3\">" +
                   "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
                   "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font>" +
                   "<font><b/><sz val=\"14\"/><name val=\"Calibri\"/></font>" +
                   "</fonts>" +
                   "<fills count=\"3\">" +
                   "<fill><patternFill patternType=\"none\"/></fill>" +
                   "<fill><patternFill patternType=\"gray125\"/></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF3B4453\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "</fills>" +
                   "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                   "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                   "<cellXfs count=\"5\">" +
                   "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
                   "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                   "<xf numFmtId=\"1\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                   "</cellXfs>" +
                   "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                   "</styleSheet>";
        }

        private static string SheetXml(Sheet sheet)
        {
            var sb = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            if (sheet.Widths != null && sheet.Widths.Length > 0)
            {
                sb.Append("<cols>");
                for (int c = 0; c < sheet.Widths.Length; c++)
                {
                    sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{sheet.Widths[c].ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
                }
                sb.Append("</cols>");
            }

            sb.Append("<sheetData>");
            for (int r = 0; r < sheet.Rows.Count; r++)
            {
                var (cells, rowStyle) = sheet.Rows[r];
                sb.Append($"<row r=\"{r + 1}\">");
                for (int c = 0; c < cells.Length; c++)
                {
                    object? value = cells[c];
                    if (value == null) continue;

                    string reference = ColumnName(c) + (r + 1).ToString(CultureInfo.InvariantCulture);
                    switch (value)
                    {
                        case int or long:
                            sb.Append($"<c r=\"{reference}\" s=\"{Pick(rowStyle, StyleInteger)}\"><v>{Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture)}</v></c>");
                            break;
                        case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                            sb.Append($"<c r=\"{reference}\" s=\"{Pick(rowStyle, StyleDecimal)}\"><v>{d.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                            break;
                        default:
                            sb.Append($"<c r=\"{reference}\" t=\"inlineStr\" s=\"{rowStyle}\"><is><t xml:space=\"preserve\">{Escape(Convert.ToString(value, CultureInfo.CurrentCulture) ?? "")}</t></is></c>");
                            break;
                    }
                }
                sb.Append("</row>");
            }
            return sb.Append("</sheetData></worksheet>").ToString();
        }

        // Títulos e cabeçalhos mantêm o próprio estilo; nas linhas normais o número define o formato
        private static int Pick(int rowStyle, int numberStyle) => rowStyle == StyleNormal ? numberStyle : rowStyle;

        private static string ColumnName(int index)
        {
            string name = "";
            for (int n = index + 1; n > 0; n = (n - 1) / 26)
            {
                name = (char)('A' + (n - 1) % 26) + name;
            }
            return name;
        }

        private static string Escape(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default:
                        // Caracteres de controle não são válidos em XML
                        if (ch >= ' ' || ch == '\t' || ch == '\n' || ch == '\r') sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
