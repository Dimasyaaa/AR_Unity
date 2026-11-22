using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Backend.Data;

namespace Backend.Services;

/// <summary>Собирает сводку проверки коробок в минимальный стандартный файл XLSX.</summary>
public static class InventoryBatchExcelWriter
{
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>Формирует Excel-книгу с одной строкой на позицию каждой коробки.</summary>
    public static byte[] Create(IReadOnlyList<InventoryBatchExportRow> rows)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(archive, "[Content_Types].xml", ContentTypesXml());
            AddText(archive, "_rels/.rels", RootRelationshipsXml());
            AddText(archive, "xl/workbook.xml", WorkbookXml());
            AddText(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml());
            AddText(archive, "xl/styles.xml", StylesXml());
            AddText(archive, "xl/worksheets/sheet1.xml", WorksheetXml(rows));
        }

        return output.ToArray();
    }

    private static string WorksheetXml(IReadOnlyList<InventoryBatchExportRow> rows)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            CloseOutput = false
        }))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("worksheet", SpreadsheetNamespace);
            writer.WriteStartElement("sheetViews", SpreadsheetNamespace);
            writer.WriteStartElement("sheetView", SpreadsheetNamespace);
            writer.WriteAttributeString("workbookViewId", "0");
            writer.WriteStartElement("pane", SpreadsheetNamespace);
            writer.WriteAttributeString("ySplit", "1");
            writer.WriteAttributeString("topLeftCell", "A2");
            writer.WriteAttributeString("activePane", "bottomLeft");
            writer.WriteAttributeString("state", "frozen");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("cols", SpreadsheetNamespace);
            int[] widths = [26, 22, 34, 16, 16, 18, 36, 26, 40, 22];
            for (int index = 0; index < widths.Length; index++)
            {
                writer.WriteStartElement("col", SpreadsheetNamespace);
                writer.WriteAttributeString("min", (index + 1).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("max", (index + 1).ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("width", widths[index].ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("customWidth", "1");
                writer.WriteEndElement();
            }
            writer.WriteEndElement();

            writer.WriteStartElement("sheetData", SpreadsheetNamespace);
            WriteRow(writer, 1,
                ["Коробка", "QR-код", "Позиция", "По учёту", "Фактически", "Статус", "Комментарий", "Сотрудник", "Отдел", "Время проверки"],
                header: true);

            for (int index = 0; index < rows.Count; index++)
            {
                InventoryBatchExportRow row = rows[index];
                string status = row.ActualQuantity == 0 ? "Нет"
                    : row.ActualQuantity < row.ExpectedQuantity ? "Частично" : "Есть";
                WriteRow(writer, index + 2,
                [
                    row.BoxTitle, row.QrCode, row.ItemName,
                    row.ExpectedQuantity.ToString(CultureInfo.InvariantCulture),
                    row.ActualQuantity.ToString(CultureInfo.InvariantCulture),
                    status, row.Comment, row.UserFullName, row.Department,
                    row.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                ], header: false, numericColumns: [4, 5]);
            }

            writer.WriteEndElement();
            if (rows.Count > 0)
            {
                writer.WriteStartElement("autoFilter", SpreadsheetNamespace);
                writer.WriteAttributeString("ref", $"A1:J{rows.Count + 1}");
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteRow(XmlWriter writer, int rowNumber, IReadOnlyList<string> values,
        bool header, int[]? numericColumns = null)
    {
        writer.WriteStartElement("row", SpreadsheetNamespace);
        writer.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
        for (int index = 0; index < values.Count; index++)
        {
            string cell = CellReference(index + 1, rowNumber);
            bool numeric = numericColumns?.Contains(index + 1) == true;
            writer.WriteStartElement("c", SpreadsheetNamespace);
            writer.WriteAttributeString("r", cell);
            if (header) writer.WriteAttributeString("s", "1");
            if (!numeric) writer.WriteAttributeString("t", "inlineStr");
            if (numeric)
            {
                writer.WriteElementString("v", SpreadsheetNamespace, values[index]);
            }
            else
            {
                writer.WriteStartElement("is", SpreadsheetNamespace);
                writer.WriteElementString("t", SpreadsheetNamespace, values[index]);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static string CellReference(int column, int row)
    {
        var label = new StringBuilder();
        while (column > 0)
        {
            column--;
            label.Insert(0, (char)('A' + column % 26));
            column /= 26;
        }
        return label.Append(row.ToString(CultureInfo.InvariantCulture)).ToString();
    }

    private static void AddText(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ContentTypesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private static string RootRelationshipsXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string WorkbookXml() => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="{SpreadsheetNamespace}" xmlns:r="{RelationshipNamespace}">
          <sheets><sheet name="Инвентаризация" sheetId="1" r:id="rId1"/></sheets>
        </workbook>
        """;

    private static string WorkbookRelationshipsXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private static string StylesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font></fonts>
          <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF175B91"/><bgColor indexed="64"/></patternFill></fill></fills>
          <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/></cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;
}
