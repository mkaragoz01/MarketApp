using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using MarketApp.DTOs;

namespace MarketApp.Services;

internal sealed record ProductImportRow(
    int RowNumber,
    string Id,
    string Name,
    string Unit,
    string Price,
    string Stock);

internal static class ProductExcelWorkbook
{
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] CreateProductsWorkbook(IReadOnlyList<ProductDto> products)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXml(archive, "[Content_Types].xml", CreateContentTypes());
            AddXml(archive, "_rels/.rels", CreateRootRelationships());
            AddXml(archive, "xl/workbook.xml", CreateWorkbook());
            AddXml(archive, "xl/_rels/workbook.xml.rels", CreateWorkbookRelationships());
            AddXml(archive, "xl/styles.xml", CreateStyles());
            AddXml(archive, "xl/worksheets/sheet1.xml", CreateProductsSheet(products));
        }

        return memory.ToArray();
    }

    public static IReadOnlyList<ProductImportRow> ReadProducts(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheetEntry = archive.GetEntry(GetFirstWorksheetPath(archive))
            ?? throw new InvalidDataException("Excel sayfası bulunamadı.");

        using var worksheetStream = worksheetEntry.Open();
        var worksheet = XDocument.Load(worksheetStream);
        var rows = worksheet.Descendants(SpreadsheetNs + "row").ToList();
        if (rows.Count < 2)
            return [];

        var headerCells = ReadCells(rows[0], sharedStrings);
        var idColumn = FindColumn(headerCells, "id", "urunid", "productid");
        var nameColumn = FindColumn(headerCells, "urunadi", "urun", "name", "productname", "product");
        var unitColumn = FindColumn(headerCells, "birim", "unit");
        var priceColumn = FindColumn(headerCells, "fiyat", "price");
        var stockColumn = FindColumn(headerCells, "stok", "stock");

        if (idColumn is null && nameColumn is null && unitColumn is null && priceColumn is null && stockColumn is null)
            throw new InvalidDataException("Excel dosyasında Id, Ürün adı, Birim, Fiyat veya Stok başlıklarından en az biri olmalı.");

        var products = new List<ProductImportRow>();
        foreach (var row in rows.Skip(1))
        {
            var rowNumber = int.TryParse(row.Attribute("r")?.Value, out var parsedRowNumber)
                ? parsedRowNumber
                : products.Count + 2;
            var cells = ReadCells(row, sharedStrings);
            var id = GetCell(cells, idColumn);
            var name = GetCell(cells, nameColumn);
            var unit = GetCell(cells, unitColumn);
            var price = GetCell(cells, priceColumn);
            var stock = GetCell(cells, stockColumn);

            if (string.IsNullOrWhiteSpace(id) &&
                string.IsNullOrWhiteSpace(name) &&
                string.IsNullOrWhiteSpace(unit) &&
                string.IsNullOrWhiteSpace(price) &&
                string.IsNullOrWhiteSpace(stock))
            {
                continue;
            }

            products.Add(new ProductImportRow(rowNumber, id, name, unit, price, stock));
        }

        return products;
    }

    private static XDocument CreateContentTypes() => new(
        new XElement(XName.Get("Types", "http://schemas.openxmlformats.org/package/2006/content-types"),
            new XElement(XName.Get("Default", "http://schemas.openxmlformats.org/package/2006/content-types"),
                new XAttribute("Extension", "rels"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(XName.Get("Default", "http://schemas.openxmlformats.org/package/2006/content-types"),
                new XAttribute("Extension", "xml"),
                new XAttribute("ContentType", "application/xml")),
            new XElement(XName.Get("Override", "http://schemas.openxmlformats.org/package/2006/content-types"),
                new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(XName.Get("Override", "http://schemas.openxmlformats.org/package/2006/content-types"),
                new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
            new XElement(XName.Get("Override", "http://schemas.openxmlformats.org/package/2006/content-types"),
                new XAttribute("PartName", "/xl/styles.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))));

    private static XDocument CreateRootRelationships() => new(
        new XElement(PackageRelationshipNs + "Relationships",
            new XElement(PackageRelationshipNs + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                new XAttribute("Target", "xl/workbook.xml"))));

    private static XDocument CreateWorkbook() => new(
        new XElement(SpreadsheetNs + "workbook",
            new XAttribute(XNamespace.Xmlns + "r", RelationshipNs),
            new XElement(SpreadsheetNs + "sheets",
                new XElement(SpreadsheetNs + "sheet",
                    new XAttribute("name", "Urunler"),
                    new XAttribute("sheetId", "1"),
                    new XAttribute(RelationshipNs + "id", "rId1")))));

    private static XDocument CreateWorkbookRelationships() => new(
        new XElement(PackageRelationshipNs + "Relationships",
            new XElement(PackageRelationshipNs + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", "worksheets/sheet1.xml")),
            new XElement(PackageRelationshipNs + "Relationship",
                new XAttribute("Id", "rId2"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                new XAttribute("Target", "styles.xml"))));

    private static XDocument CreateStyles() => new(
        new XElement(SpreadsheetNs + "styleSheet",
            new XElement(SpreadsheetNs + "fonts",
                new XAttribute("count", "1"),
                new XElement(SpreadsheetNs + "font")),
            new XElement(SpreadsheetNs + "fills",
                new XAttribute("count", "1"),
                new XElement(SpreadsheetNs + "fill")),
            new XElement(SpreadsheetNs + "borders",
                new XAttribute("count", "1"),
                new XElement(SpreadsheetNs + "border")),
            new XElement(SpreadsheetNs + "cellStyleXfs",
                new XAttribute("count", "1"),
                new XElement(SpreadsheetNs + "xf")),
            new XElement(SpreadsheetNs + "cellXfs",
                new XAttribute("count", "1"),
                new XElement(SpreadsheetNs + "xf"))));

    private static XDocument CreateProductsSheet(IReadOnlyList<ProductDto> products)
    {
        var rows = new List<XElement>
        {
            CreateRow(1,
                TextCell("A", 1, "Id"),
                TextCell("B", 1, "Ürün adı"),
                TextCell("C", 1, "Birim"),
                TextCell("D", 1, "Fiyat"),
                TextCell("E", 1, "Stok"))
        };

        for (var i = 0; i < products.Count; i++)
        {
            var product = products[i];
            var rowNumber = i + 2;
            rows.Add(CreateRow(rowNumber,
                NumberCell("A", rowNumber, product.Id),
                TextCell("B", rowNumber, product.Name),
                TextCell("C", rowNumber, product.Unit),
                NumberCell("D", rowNumber, product.Price),
                NumberCell("E", rowNumber, product.Stock)));
        }

        return new XDocument(
            new XElement(SpreadsheetNs + "worksheet",
                new XElement(SpreadsheetNs + "cols",
                    new XElement(SpreadsheetNs + "col", new XAttribute("min", "1"), new XAttribute("max", "1"), new XAttribute("width", "8"), new XAttribute("customWidth", "1")),
                    new XElement(SpreadsheetNs + "col", new XAttribute("min", "2"), new XAttribute("max", "2"), new XAttribute("width", "28"), new XAttribute("customWidth", "1")),
                    new XElement(SpreadsheetNs + "col", new XAttribute("min", "3"), new XAttribute("max", "5"), new XAttribute("width", "14"), new XAttribute("customWidth", "1"))),
                new XElement(SpreadsheetNs + "sheetData", rows)));
    }

    private static XElement CreateRow(int rowNumber, params XElement[] cells) =>
        new(SpreadsheetNs + "row", new XAttribute("r", rowNumber), cells);

    private static XElement TextCell(string column, int row, string value) =>
        new(SpreadsheetNs + "c",
            new XAttribute("r", $"{column}{row}"),
            new XAttribute("t", "inlineStr"),
            new XElement(SpreadsheetNs + "is",
                new XElement(SpreadsheetNs + "t", value)));

    private static XElement NumberCell(string column, int row, decimal value) =>
        new(SpreadsheetNs + "c",
            new XAttribute("r", $"{column}{row}"),
            new XElement(SpreadsheetNs + "v", value.ToString(CultureInfo.InvariantCulture)));

    private static void AddXml(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return [];

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document
            .Descendants(SpreadsheetNs + "si")
            .Select(si => string.Concat(si.Descendants(SpreadsheetNs + "t").Select(t => t.Value)))
            .ToList();
    }

    private static string GetFirstWorksheetPath(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relationshipsEntry is null)
            return "xl/worksheets/sheet1.xml";

        using var workbookStream = workbookEntry.Open();
        using var relationshipsStream = relationshipsEntry.Open();
        var workbook = XDocument.Load(workbookStream);
        var relationships = XDocument.Load(relationshipsStream);
        var firstSheetRelId = workbook.Descendants(SpreadsheetNs + "sheet")
            .FirstOrDefault()
            ?.Attribute(RelationshipNs + "id")
            ?.Value;

        if (string.IsNullOrWhiteSpace(firstSheetRelId))
            return "xl/worksheets/sheet1.xml";

        var target = relationships
            .Descendants(PackageRelationshipNs + "Relationship")
            .FirstOrDefault(r => r.Attribute("Id")?.Value == firstSheetRelId)
            ?.Attribute("Target")
            ?.Value;

        if (string.IsNullOrWhiteSpace(target))
            return "xl/worksheets/sheet1.xml";

        target = target.Replace('\\', '/').TrimStart('/');
        return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? target
            : $"xl/{target}";
    }

    private static Dictionary<string, string> ReadCells(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in row.Elements(SpreadsheetNs + "c"))
        {
            var reference = cell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            cells[GetColumnName(reference)] = ReadCellValue(cell, sharedStrings);
        }

        return cells;
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "s")
        {
            var indexText = cell.Element(SpreadsheetNs + "v")?.Value;
            return int.TryParse(indexText, out var index) && index >= 0 && index < sharedStrings.Count
                ? sharedStrings[index]
                : string.Empty;
        }

        if (type == "inlineStr")
            return cell.Descendants(SpreadsheetNs + "t").FirstOrDefault()?.Value ?? string.Empty;

        return cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
    }

    private static string? FindColumn(Dictionary<string, string> headerCells, params string[] aliases)
    {
        foreach (var (column, value) in headerCells)
        {
            var normalized = NormalizeHeader(value);
            if (aliases.Contains(normalized))
                return column;
        }

        return null;
    }

    private static string GetCell(Dictionary<string, string> cells, string? column) =>
        column is not null && cells.TryGetValue(column, out var value)
            ? value.Trim()
            : string.Empty;

    private static string GetColumnName(string cellReference) =>
        new(cellReference.TakeWhile(char.IsLetter).ToArray());

    private static string NormalizeHeader(string value) =>
        value.Trim()
            .ToLowerInvariant()
            .Replace("ı", "i", StringComparison.Ordinal)
            .Replace("ü", "u", StringComparison.Ordinal)
            .Replace("ö", "o", StringComparison.Ordinal)
            .Replace("ş", "s", StringComparison.Ordinal)
            .Replace("ğ", "g", StringComparison.Ordinal)
            .Replace("ç", "c", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
}
