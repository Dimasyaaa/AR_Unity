using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Backend.Data;

namespace Backend.Services;

/// <summary>Заполняет исходный бланк описи результатами общей инвентаризации.</summary>
public static class InventoryBatchExcelWriter
{
    private const string TemplateResourceName = "Backend.InventoryOpisTemplate.xlsx";
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const int FirstItemRow = 11;
    private const int LastTemplateItemRow = 118;
    private const int TemplateItemRowCount = LastTemplateItemRow - FirstItemRow + 1;
    private const int FirstFooterRow = LastTemplateItemRow + 2;
    private const int ExcelMaximumRow = 1_048_576;

    private static readonly XNamespace SheetNamespace = SpreadsheetNamespace;
    private static readonly Regex CellReferencePattern = new(@"(?<column>\$?[A-Z]{1,3})(?<row>\$?\d+)", RegexOptions.Compiled);
    private static readonly Regex RangeReferencePattern = new(
        @"\$?[A-Z]{1,3}\$?\d+(?::\$?[A-Z]{1,3}\$?\d+)?", RegexOptions.Compiled);

    /// <summary>Заполняет два листа исходной описи, не меняя их макет и оформление.</summary>
    public static byte[] Create(IReadOnlyList<InventoryBatchExportRow> rows, byte[]? workbookTemplate = null)
    {
        if (rows.Count == 0)
            throw new ArgumentException("Нельзя сформировать опись без строк инвентаризации.", nameof(rows));

        DateTime inventoryDate = rows.Max(row => row.CompletedAt).ToLocalTime().Date;
        List<string> divisions = GetDistinctValues(rows.Select(row => row.StructuralDivision));
        List<string> molNames = GetDistinctValues(rows.Select(row => row.Mol));

        using Stream template = OpenTemplate(workbookTemplate);
        using var sourceArchive = new ZipArchive(template, ZipArchiveMode.Read, leaveOpen: true);
        List<string> sharedStrings = ReadSharedStrings(sourceArchive);
        using var output = new MemoryStream();
        using (var resultArchive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in sourceArchive.Entries)
            {
                ZipArchiveEntry destination = resultArchive.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using Stream source = entry.Open();
                using Stream target = destination.Open();

                if (entry.FullName == "xl/worksheets/sheet1.xml")
                {
                    XDocument sheet = XDocument.Load(source, LoadOptions.PreserveWhitespace);
                    FillReceiptSheet(sheet, inventoryDate, divisions, molNames, sharedStrings);
                    sheet.Save(target, SaveOptions.DisableFormatting);
                }
                else if (entry.FullName == "xl/worksheets/sheet2.xml")
                {
                    XDocument sheet = XDocument.Load(source, LoadOptions.PreserveWhitespace);
                    FillCountSheet(sheet, rows, inventoryDate, molNames);
                    sheet.Save(target, SaveOptions.DisableFormatting);
                }
                else
                {
                    source.CopyTo(target);
                }
            }
        }

        return output.ToArray();
    }

    /// <summary>Открывает шаблон, включённый в сборку, чтобы серверу не нужен был отдельный файл рядом.</summary>
    private static Stream OpenTemplate(byte[]? workbookTemplate)
    {
        if (workbookTemplate is { Length: > 0 })
            return new MemoryStream(workbookTemplate, writable: false);

        return typeof(InventoryBatchExcelWriter).Assembly.GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException("В сборку не включён шаблон инвентаризационной описи.");
    }

    /// <summary>Считывает общие текстовые значения исходной книги для замены текста в её ячейках.</summary>
    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return new List<string>();
        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        return document.Root?.Elements(SheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SheetNamespace + "t").Select(text => text.Value)))
            .ToList() ?? new List<string>();
    }

    /// <summary>Заполняет дату, подразделение и известное имя МОЛ на листе-расписке.</summary>
    private static void FillReceiptSheet(XDocument sheet, DateTime date, IReadOnlyList<string> divisions,
        IReadOnlyList<string> molNames, IReadOnlyList<string> sharedStrings)
    {
        SetText(sheet, "B6", string.Join(", ", divisions));
        SetText(sheet, "A15", $"«{date.Day:00}»");
        SetText(sheet, "E15", GetMonthName(date.Month));
        SetText(sheet, "H15", $"{date.Year} г.");

        string declaration = GetText(sheet, "A20", sharedStrings);
        SetText(sheet, "A20", declaration.Replace("за ______ год", $"за {date.Year} год", StringComparison.Ordinal));

        // Имя МОЛ вписываем только тогда, когда оно однозначно для всей проверки.
        if (molNames.Count == 1)
            SetText(sheet, "G25", molNames[0]);
    }

    /// <summary>Заполняет реквизиты и десять столбцов бланка подсчёта.</summary>
    private static void FillCountSheet(XDocument sheet, IReadOnlyList<InventoryBatchExportRow> rows,
        DateTime date, IReadOnlyList<string> molNames)
    {
        SetNumber(sheet, "D7", ToExcelDate(date));

        int extraRows = Math.Max(0, rows.Count - TemplateItemRowCount);
        EnsureItemRows(sheet, rows.Count);

        for (int index = 0; index < rows.Count; index++)
        {
            InventoryBatchExportRow row = rows[index];
            int rowNumber = FirstItemRow + index;
            SetText(sheet, $"A{rowNumber}", row.StructuralDivision);
            SetText(sheet, $"B{rowNumber}", row.Warehouse);
            SetText(sheet, $"C{rowNumber}", row.ItemName);
            SetText(sheet, $"D{rowNumber}", row.EnsCode);
            SetText(sheet, $"E{rowNumber}", row.AccountCode);
            SetText(sheet, $"F{rowNumber}", row.Mol);
            SetText(sheet, $"G{rowNumber}", row.StorageLocation);
            SetText(sheet, $"H{rowNumber}", row.Unit);
            SetNumber(sheet, $"I{rowNumber}", row.ActualQuantity);
            SetText(sheet, $"J{rowNumber}", row.Comment);
        }

        // В нижней строке бланка оставляем имя МОЛ; подпись и должность сотрудник заполнит сам.
        if (molNames.Count == 1)
            SetText(sheet, $"H{129 + extraRows}", molNames[0]);
    }

    /// <summary>Добавляет строки таблицы перед подписью комиссии, если данных больше вместимости бланка.</summary>
    private static void EnsureItemRows(XDocument sheet, int itemCount)
    {
        int extraRows = itemCount - TemplateItemRowCount;
        if (extraRows <= 0) return;
        if (extraRows > ExcelMaximumRow - FirstFooterRow)
            throw new InvalidOperationException("Количество позиций превышает допустимый размер листа Excel.");

        XElement root = sheet.Root ?? throw new InvalidDataException("Лист Excel не содержит корневого элемента.");
        XElement sheetData = root.Element(SheetNamespace + "sheetData")
            ?? throw new InvalidDataException("В шаблоне не найден блок строк листа.");

        // Сдвигаем нижнюю часть листа, освобождая место перед последней строкой таблицы.
        foreach (XElement row in sheetData.Elements(SheetNamespace + "row").ToList())
        {
            int currentRow = GetRowNumber(row);
            if (currentRow >= LastTemplateItemRow)
                SetRowNumber(row, currentRow + extraRows);
        }

        XElement bodyRow = sheetData.Elements(SheetNamespace + "row")
            .FirstOrDefault(row => GetRowNumber(row) == LastTemplateItemRow - 2)
            ?? throw new InvalidDataException("В шаблоне не найдена строка для продолжения таблицы.");

        for (int index = 0; index < extraRows; index++)
        {
            int insertedRowNumber = LastTemplateItemRow + index;
            XElement insertedRow = new(bodyRow);
            SetRowNumber(insertedRow, insertedRowNumber);
            XElement beforeRow = sheetData.Elements(SheetNamespace + "row")
                .First(row => GetRowNumber(row) == LastTemplateItemRow + extraRows);
            beforeRow.AddBeforeSelf(insertedRow);
        }

        ShiftWorksheetReferences(root, LastTemplateItemRow, extraRows);
    }

    /// <summary>Обновляет ссылки на строки после расширения бланка.</summary>
    private static void ShiftWorksheetReferences(XElement root, int firstShiftedRow, int offset)
    {
        foreach (XAttribute attribute in root.DescendantsAndSelf().Attributes().ToList())
        {
            if (attribute.Name.LocalName is not ("ref" or "sqref")) continue;
            attribute.Value = RangeReferencePattern.Replace(attribute.Value,
                range => ShiftRange(range.Value, firstShiftedRow, offset));
        }

        foreach (XElement breakRow in root.Descendants(SheetNamespace + "brk"))
        {
            XAttribute? id = breakRow.Attribute("id");
            if (id is not null && int.TryParse(id.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int row)
                && row >= firstShiftedRow)
                id.Value = (row + offset).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string ShiftRange(string reference, int firstShiftedRow, int offset) =>
        CellReferencePattern.Replace(reference, match =>
        {
            string rowText = match.Groups["row"].Value;
            bool absolute = rowText.StartsWith('$');
            string digits = absolute ? rowText[1..] : rowText;
            if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int row) || row < firstShiftedRow)
                return match.Value;

            string shiftedRow = (row + offset).ToString(CultureInfo.InvariantCulture);
            return match.Groups["column"].Value + (absolute ? "$" : string.Empty) + shiftedRow;
        });

    private static int GetRowNumber(XElement row) =>
        int.Parse((string?)row.Attribute("r") ?? "0", CultureInfo.InvariantCulture);

    private static void SetRowNumber(XElement row, int rowNumber)
    {
        row.SetAttributeValue("r", rowNumber.ToString(CultureInfo.InvariantCulture));
        foreach (XElement cell in row.Elements(SheetNamespace + "c"))
        {
            XAttribute? reference = cell.Attribute("r");
            if (reference is not null)
                reference.Value = ShiftRange(reference.Value, 0, rowNumber - GetCellRow(reference.Value));
        }
    }

    private static int GetCellRow(string reference)
    {
        Match match = CellReferencePattern.Match(reference);
        return match.Success
            ? int.Parse(match.Groups["row"].Value.TrimStart('$'), CultureInfo.InvariantCulture)
            : 0;
    }

    /// <summary>Возвращает текст ячейки, в том числе если исходный файл хранит его в общем списке строк.</summary>
    private static string GetText(XDocument sheet, string reference, IReadOnlyList<string> sharedStrings)
    {
        XElement cell = FindCell(sheet, reference);
        XElement? inlineText = cell.Element(SheetNamespace + "is");
        if (inlineText is not null)
            return string.Concat(inlineText.Descendants(SheetNamespace + "t").Select(text => text.Value));

        string value = cell.Element(SheetNamespace + "v")?.Value ?? string.Empty;
        if ((string?)cell.Attribute("t") == "s" &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) &&
            index >= 0 && index < sharedStrings.Count)
            return sharedStrings[index];

        return value;
    }

    private static void SetText(XDocument sheet, string reference, string? value)
    {
        XElement cell = FindCell(sheet, reference);
        cell.SetAttributeValue("t", "inlineStr");
        cell.RemoveNodes();
        cell.Add(new XElement(SheetNamespace + "is",
            new XElement(SheetNamespace + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value ?? string.Empty)));
    }

    private static void SetNumber(XDocument sheet, string reference, int value) =>
        SetNumber(sheet, reference, value.ToString(CultureInfo.InvariantCulture));

    private static void SetNumber(XDocument sheet, string reference, double value) =>
        SetNumber(sheet, reference, value.ToString("R", CultureInfo.InvariantCulture));

    private static void SetNumber(XDocument sheet, string reference, string value)
    {
        XElement cell = FindCell(sheet, reference);
        cell.Attribute("t")?.Remove();
        cell.RemoveNodes();
        cell.Add(new XElement(SheetNamespace + "v", value));
    }

    private static XElement FindCell(XDocument sheet, string reference) =>
        sheet.Root?.Descendants(SheetNamespace + "c")
            .FirstOrDefault(cell => (string?)cell.Attribute("r") == reference)
        ?? throw new InvalidDataException($"В шаблоне не найдена ячейка {reference}.");

    private static double ToExcelDate(DateTime date) =>
        (date.Date - new DateTime(1899, 12, 30)).TotalDays;

    private static string GetMonthName(int month) => month switch
    {
        1 => "января", 2 => "февраля", 3 => "марта", 4 => "апреля",
        5 => "мая", 6 => "июня", 7 => "июля", 8 => "августа",
        9 => "сентября", 10 => "октября", 11 => "ноября", 12 => "декабря",
        _ => throw new ArgumentOutOfRangeException(nameof(month))
    };

    private static List<string> GetDistinctValues(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
