using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Backend.Services;

/// <summary>Строка товарно-материальной описи, прочитанная из листа подсчёта.</summary>
public sealed record InventoryOpisRow(
    int SourceRowNumber,
    string StructuralDivision,
    string Warehouse,
    string ItemName,
    string EnsCode,
    string AccountCode,
    string Mol,
    string StorageLocation,
    string Unit,
    int? AccountingQuantity);

/// <summary>Читает стандартную двухлистовую опись, не требуя Excel на сервере.</summary>
public static class InventoryOpisExcelReader
{
    private static readonly XNamespace MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly Regex CellAddressPattern = new(@"^(?<column>[A-Z]+)(?<row>\d+)$", RegexOptions.Compiled);
    private static readonly Regex ZeroFormatPattern = new(@"^0+$", RegexOptions.Compiled);

    /// <summary>Извлекает позиции из листа «Бланк подсчёта», начиная с 11-й строки.</summary>
    public static List<InventoryOpisRow> Read(byte[] workbookBytes)
    {
        if (workbookBytes.Length == 0)
            throw new InvalidDataException("Выбранный файл пуст.");

        using var memory = new MemoryStream(workbookBytes, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        List<string> sharedStrings = ReadSharedStrings(archive);
        List<string> numberFormats = ReadNumberFormats(archive);
        string sheetPath = FindCountSheetPath(archive);
        ZipArchiveEntry sheetEntry = archive.GetEntry(sheetPath)
            ?? throw new InvalidDataException("В Excel-файле не найден лист подсчёта.");

        using Stream sheetStream = sheetEntry.Open();
        XDocument sheet = XDocument.Load(sheetStream);
        XElement sheetData = sheet.Root?.Element(MainNamespace + "sheetData")
            ?? throw new InvalidDataException("Лист подсчёта не содержит таблицу строк.");

        var result = new List<InventoryOpisRow>();
        foreach (XElement row in sheetData.Elements(MainNamespace + "row"))
        {
            if (!int.TryParse((string?)row.Attribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out int rowNumber)
                || rowNumber < 11)
                continue;

            Dictionary<string, XElement> cells = row.Elements(MainNamespace + "c")
                .Select(cell => (CellAddressPattern.Match((string?)cell.Attribute("r") ?? string.Empty), cell))
                .Where(pair => pair.Item1.Success)
                .ToDictionary(pair => pair.Item1.Groups["column"].Value, pair => pair.cell, StringComparer.Ordinal);

            string itemName = ReadCell(cells, "C", sharedStrings, numberFormats).Trim();
            string ensCode = ReadCell(cells, "D", sharedStrings, numberFormats).Trim();
            if (itemName.Length == 0 && ensCode.Length == 0)
                continue;

            string quantityText = ReadCell(cells, "I", sharedStrings, numberFormats).Trim();
            int? accountingQuantity = null;
            if (quantityText.Length > 0)
            {
                if (!int.TryParse(quantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity))
                    throw new InvalidDataException($"В строке {rowNumber} количество должно быть целым числом.");
                accountingQuantity = quantity;
            }

            result.Add(new InventoryOpisRow(
                rowNumber,
                ReadCell(cells, "A", sharedStrings, numberFormats).Trim(),
                ReadCell(cells, "B", sharedStrings, numberFormats).Trim(),
                itemName,
                ensCode,
                ReadCell(cells, "E", sharedStrings, numberFormats).Trim(),
                ReadCell(cells, "F", sharedStrings, numberFormats).Trim(),
                ReadCell(cells, "G", sharedStrings, numberFormats).Trim(),
                ReadCell(cells, "H", sharedStrings, numberFormats).Trim(),
                accountingQuantity));
        }

        if (result.Count == 0)
            throw new InvalidDataException("В листе «Бланк подсчёта» не найдены строки с наименованием и ЕНС.");
        if (result.Count > 10000)
            throw new InvalidDataException("В одной описи допускается не более 10000 позиций.");
        return result;
    }

    /// <summary>Находит лист подсчёта по его имени, а для старых книг использует второй лист.</summary>
    private static string FindCountSheetPath(ZipArchive archive)
    {
        ZipArchiveEntry? workbookEntry = archive.GetEntry("xl/workbook.xml");
        ZipArchiveEntry? relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is not null && relationshipsEntry is not null)
        {
            using Stream workbookStream = workbookEntry.Open();
            using Stream relationshipsStream = relationshipsEntry.Open();
            XDocument workbook = XDocument.Load(workbookStream);
            XDocument relationships = XDocument.Load(relationshipsStream);
            XElement? countSheet = workbook.Descendants(MainNamespace + "sheet")
                .FirstOrDefault(sheet => string.Equals((string?)sheet.Attribute("name"), "Бланк подсчёта", StringComparison.OrdinalIgnoreCase)
                    || string.Equals((string?)sheet.Attribute("name"), "Бланк подсчета", StringComparison.OrdinalIgnoreCase));
            string? relationId = (string?)countSheet?.Attribute(RelationshipNamespace + "id");
            string? target = relationships.Root?.Elements(PackageRelationshipNamespace + "Relationship")
                .Where(relation => (string?)relation.Attribute("Id") == relationId)
                .Select(relation => (string?)relation.Attribute("Target"))
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(target))
            {
                string normalized = target.StartsWith('/')
                    ? target.TrimStart('/')
                    : "xl/" + target.TrimStart('.', '/');
                normalized = normalized.Replace("\\", "/", StringComparison.Ordinal);
                if (archive.GetEntry(normalized) is not null)
                    return normalized;
            }
        }

        return archive.GetEntry("xl/worksheets/sheet2.xml") is not null
            ? "xl/worksheets/sheet2.xml"
            : throw new InvalidDataException("Не удалось определить лист «Бланк подсчёта».");
    }

    /// <summary>Загружает общие текстовые значения книги; текстовые ячейки могут ссылаться на этот список.</summary>
    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return new List<string>();
        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        return document.Root?.Elements(MainNamespace + "si")
            .Select(item => string.Concat(item.Descendants(MainNamespace + "t").Select(text => text.Value)))
            .ToList() ?? new List<string>();
    }

    /// <summary>Читает пользовательские форматы нулей, чтобы не терять ведущие нули в кодах.</summary>
    private static List<string> ReadNumberFormats(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/styles.xml");
        if (entry is null) return new List<string>();
        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        Dictionary<uint, string> customFormats = document.Root?.Element(MainNamespace + "numFmts")?
            .Elements(MainNamespace + "numFmt")
            .Where(format => uint.TryParse((string?)format.Attribute("numFmtId"), out _))
            .ToDictionary(
                format => uint.Parse((string)format.Attribute("numFmtId")!, CultureInfo.InvariantCulture),
                format => (string?)format.Attribute("formatCode") ?? string.Empty)
            ?? new Dictionary<uint, string>();

        return document.Root?.Element(MainNamespace + "cellXfs")?
            .Elements(MainNamespace + "xf")
            .Select(format => uint.TryParse((string?)format.Attribute("numFmtId"), out uint id)
                && customFormats.TryGetValue(id, out string? code) ? code : string.Empty)
            .ToList() ?? new List<string>();
    }

    /// <summary>Возвращает текст ячейки с поддержкой shared strings и простых числовых масок.</summary>
    private static string ReadCell(Dictionary<string, XElement> cells, string column,
        IReadOnlyList<string> sharedStrings, IReadOnlyList<string> numberFormats)
    {
        if (!cells.TryGetValue(column, out XElement? cell)) return string.Empty;
        string rawValue = cell.Element(MainNamespace + "v")?.Value ?? string.Empty;
        string? cellType = (string?)cell.Attribute("t");

        if (cellType == "s" && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            && index >= 0 && index < sharedStrings.Count)
            return sharedStrings[index];

        XElement? inlineText = cell.Element(MainNamespace + "is");
        if (inlineText is not null)
            return string.Concat(inlineText.Descendants(MainNamespace + "t").Select(text => text.Value));

        if (cellType is "str" or "e") return rawValue;
        if (cellType == "b") return rawValue == "1" ? "TRUE" : "FALSE";

        if (int.TryParse((string?)cell.Attribute("s"), NumberStyles.None, CultureInfo.InvariantCulture, out int styleIndex)
            && styleIndex >= 0 && styleIndex < numberFormats.Count
            && ZeroFormatPattern.IsMatch(numberFormats[styleIndex])
            && long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
            return integer.ToString("D" + numberFormats[styleIndex].Length, CultureInfo.InvariantCulture);

        return rawValue;
    }
}
