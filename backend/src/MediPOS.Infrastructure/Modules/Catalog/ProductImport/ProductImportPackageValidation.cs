using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog.ProductImport;

namespace MediPOS.Infrastructure.Modules.Catalog.ProductImport;

internal static class ProductImportPackageValidation
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly HashSet<string> FixedParts = new(StringComparer.Ordinal)
    {
        "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels",
        "xl/styles.xml", "xl/sharedStrings.xml", "docProps/core.xml", "docProps/app.xml", "docProps/custom.xml",
    };
    private static readonly HashSet<string> ContentTypes = new(StringComparer.Ordinal)
    {
        "application/xml", "application/vnd.openxmlformats-package.relationships+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml",
        "application/vnd.openxmlformats-officedocument.theme+xml",
        "application/vnd.openxmlformats-package.core-properties+xml",
        "application/vnd.openxmlformats-officedocument.extended-properties+xml",
        "application/vnd.openxmlformats-officedocument.custom-properties+xml",
    };
    private static readonly HashSet<string> ActiveElements = new(StringComparer.Ordinal)
    { "f", "formula", "formula1", "formula2", "oleObjects", "controls", "externalReferences", "connections" };
    private static readonly HashSet<string> StyleCollections = new(StringComparer.Ordinal)
    { "fonts", "fills", "borders", "cellStyleXfs", "cellXfs", "cellStyles", "dxfs", "numFmts" };

    internal static Dictionary<(int Row, int Column), string> Validate(Stream stream, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        if (archive.Entries.Count > ProductImportLimits.MaximumPackageEntries) Fail(ProductImportErrors.FileLimit);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        var worksheetCount = 0;
        var numeric = new Dictionary<(int, int), string>();
        var sharedStringReferences = new List<int>();
        var sharedStringCount = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!names.Add(entry.FullName)) Fail(ProductImportErrors.InvalidFile);
            if (entry.Length > ProductImportLimits.MaximumPartBytes) Fail(ProductImportErrors.FileLimit);
            expanded += entry.Length;
            if (expanded > ProductImportLimits.MaximumExpandedBytes) Fail(ProductImportErrors.FileLimit);
            var worksheet = IsNumberedPart(entry.FullName, "xl/worksheets/sheet", ".xml");
            if (!FixedParts.Contains(entry.FullName) && !worksheet && !IsNumberedPart(entry.FullName, "xl/theme/theme", ".xml") &&
                !IsCoreProperties(entry.FullName))
                Fail(ProductImportErrors.UnsafeWorkbook);
            using var contents = entry.Open();
            using var bounded = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = contents.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (bounded.Length + read > entry.Length || bounded.Length + read > ProductImportLimits.MaximumPartBytes)
                    Fail(ProductImportErrors.FileLimit);
                bounded.Write(buffer, 0, read);
            }
            if (bounded.Length != entry.Length) Fail(ProductImportErrors.InvalidFile);
            bounded.Position = 0;
            using var reader = XmlReader.Create(bounded, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = ProductImportLimits.MaximumPartBytes,
            });
            var document = XDocument.Load(reader);
            var nodes = 0;
            foreach (var element in document.Descendants())
            {
                if (++nodes > ProductImportLimits.MaximumXmlNodes) Fail(ProductImportErrors.FileLimit);
                if (ActiveElements.Contains(element.Name.LocalName)) Fail(ProductImportErrors.UnsafeWorkbook);
            }
            if (entry.FullName.EndsWith(".rels", StringComparison.Ordinal))
            {
                foreach (var relation in document.Descendants().Where(value => value.Name.LocalName == "Relationship"))
                {
                    var target = (string?)relation.Attribute("Target") ?? string.Empty;
                    if (string.Equals((string?)relation.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase) ||
                        target.Contains(':', StringComparison.Ordinal) || target.StartsWith("//", StringComparison.Ordinal) || target.Contains('\\'))
                        Fail(ProductImportErrors.UnsafeWorkbook);
                }
            }
            if (entry.FullName == "[Content_Types].xml")
            {
                var types = document.Descendants().Attributes("ContentType").Select(value => value.Value).ToArray();
                if (types.Any(value => !ContentTypes.Contains(value))) Fail(ProductImportErrors.UnsafeWorkbook);
                if (!types.Contains("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", StringComparer.Ordinal))
                    Fail(ProductImportErrors.InvalidFile);
            }
            if (entry.FullName == "xl/workbook.xml")
            {
                var sheets = document.Descendants(Spreadsheet + "sheet").ToArray();
                if (sheets.Length != 1 || (string?)sheets[0].Attribute("name") != ProductImportTemplate.SheetName)
                    Fail(ProductImportErrors.TemplateVersion);
            }
            if (entry.FullName == "xl/styles.xml")
                foreach (var collection in document.Descendants().Where(value => StyleCollections.Contains(value.Name.LocalName)))
                    if (collection.Elements().Take(ProductImportLimits.MaximumStyles + 1).Count() > ProductImportLimits.MaximumStyles)
                        Fail(ProductImportErrors.FileLimit);
            if (entry.FullName == "xl/sharedStrings.xml")
            {
                foreach (var value in document.Descendants(Spreadsheet + "si"))
                {
                    if (++sharedStringCount > ProductImportLimits.MaximumWorksheetRow * ProductImportLimits.MaximumColumns ||
                        value.Descendants(Spreadsheet + "t").Sum(part => part.Value.Length) > ProductImportLimits.MaximumCellCharacters)
                        Fail(ProductImportErrors.FileLimit);
                }
            }
            if (worksheet)
            {
                worksheetCount++;
                ValidateWorksheet(document, numeric, sharedStringReferences);
            }
        }
        if (worksheetCount != 1 || !names.Contains("[Content_Types].xml") || !names.Contains("xl/workbook.xml") ||
            !names.Contains("_rels/.rels") || !names.Contains("xl/_rels/workbook.xml.rels"))
            Fail(ProductImportErrors.InvalidFile);
        if (sharedStringReferences.Any(value => value < 0 || value >= sharedStringCount)) Fail(ProductImportErrors.InvalidFile);
        stream.Position = 0;
        return numeric;
    }

    private static void ValidateWorksheet(XDocument document, Dictionary<(int, int), string> numeric, List<int> sharedStringReferences)
    {
        if (document.Root?.Name != Spreadsheet + "worksheet") Fail(ProductImportErrors.InvalidFile);
        var dimensions = document.Descendants(Spreadsheet + "dimension").Select(value => (string?)value.Attribute("ref"));
        foreach (var dimension in dimensions) ValidateRange(dimension);
        foreach (var column in document.Descendants(Spreadsheet + "col"))
            if (!int.TryParse((string?)column.Attribute("max"), CultureInfo.InvariantCulture, out var max) ||
                max > ProductImportLimits.MaximumColumns || max < 1) Fail(ProductImportErrors.FileLimit);
        foreach (var merged in document.Descendants(Spreadsheet + "mergeCell"))
        {
            var range = (string?)merged.Attribute("ref");
            ValidateRange(range);
            if (Address(range!.Split(':')[^1]).Row >= ProductImportTemplate.HeaderRow) Fail(ProductImportErrors.InvalidFile);
        }
        var rowNumbers = new HashSet<int>();
        var addresses = new HashSet<(int, int)>();
        foreach (var row in document.Descendants(Spreadsheet + "row"))
        {
            if (!int.TryParse((string?)row.Attribute("r"), CultureInfo.InvariantCulture, out var number) ||
                number < 1 || number > ProductImportLimits.MaximumWorksheetRow) Fail(ProductImportErrors.FileLimit);
            if (!rowNumbers.Add(number)) Fail(ProductImportErrors.InvalidFile);
            foreach (var cell in row.Elements(Spreadsheet + "c"))
            {
                var address = Address((string?)cell.Attribute("r"));
                if (address.Row != number || !addresses.Add(address)) Fail(ProductImportErrors.InvalidFile);
                var value = cell.Element(Spreadsheet + "v")?.Value ?? string.Empty;
                if (value.Length > ProductImportLimits.MaximumCellCharacters ||
                    cell.Descendants(Spreadsheet + "t").Sum(part => part.Value.Length) > ProductImportLimits.MaximumCellCharacters)
                    Fail(ProductImportErrors.FileLimit);
                var type = (string?)cell.Attribute("t");
                if (type == "s")
                {
                    if (!int.TryParse(value, CultureInfo.InvariantCulture, out var index)) Fail(ProductImportErrors.InvalidFile);
                    sharedStringReferences.Add(index);
                }
                else if ((type is null or "n") && value.Length > 0) numeric.Add(address, value);
            }
        }
    }

    private static void ValidateRange(string? range)
    {
        if (string.IsNullOrEmpty(range)) Fail(ProductImportErrors.InvalidFile);
        foreach (var part in range!.Split(':')) Address(part);
    }

    private static (int Row, int Column) Address(string? address)
    {
        if (string.IsNullOrEmpty(address) || address.Length > 10) Fail(ProductImportErrors.InvalidFile);
        var column = 0;
        var position = 0;
        while (position < address!.Length && address[position] is >= 'A' and <= 'Z')
        {
            column = column * 26 + address[position++] - 'A' + 1;
            if (column > ProductImportLimits.MaximumColumns) Fail(ProductImportErrors.FileLimit);
        }
        if (column == 0) Fail(ProductImportErrors.FileLimit);
        if (!int.TryParse(address.AsSpan(position), NumberStyles.None, CultureInfo.InvariantCulture, out var row) ||
            row < 1 || row > ProductImportLimits.MaximumWorksheetRow) Fail(ProductImportErrors.FileLimit);
        return (row, column);
    }

    private static bool IsNumberedPart(string path, string prefix, string suffix) => path.StartsWith(prefix, StringComparison.Ordinal) &&
        path.EndsWith(suffix, StringComparison.Ordinal) && int.TryParse(path.AsSpan(prefix.Length, path.Length - prefix.Length - suffix.Length),
            NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0;

    private static bool IsCoreProperties(string path)
    {
        const string prefix = "package/services/metadata/core-properties/";
        const string suffix = ".psmdcp";
        return path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(suffix, StringComparison.Ordinal) &&
            path.Length == prefix.Length + 32 + suffix.Length && Guid.TryParseExact(path.AsSpan(prefix.Length, 32), "N", out _);
    }

    [DoesNotReturn]
    private static void Fail(ApplicationError error) => throw new ApplicationErrorException(error);
}
