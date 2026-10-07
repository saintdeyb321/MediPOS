using System.Globalization;
using System.Numerics;
using MediPOS.Application.Errors;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Application.Modules.Catalog.ProductImport;

public sealed record ValidatedProductImportRow(int RowNumber, BusinessProduct? Product, ProductUnit? BaseUnit, ApplicationError? Error);

internal static class ProductImportRowValidation
{
    internal static ValidatedProductImportRow Validate(ProductImportRow row, Guid tenantId, DateTimeOffset now,
        IReadOnlySet<Guid> activeCategories, IReadOnlySet<string> conflictingCodes)
    {
        ValidatedProductImportRow Invalid(ApplicationError error) => new(row.RowNumber, null, null, error);
        if (row.CellError is not null) return Invalid(row.CellError);
        var code = row.InternalCode?.Trim();
        if (string.IsNullOrEmpty(code)) return Invalid(ProductImportErrors.MissingCode);
        if (conflictingCodes.Contains(code)) return Invalid(ProductImportErrors.DuplicateCode);
        var type = row.ProductType?.Trim().ToLowerInvariant() switch
        {
            "medicine" => ProductType.Medicine,
            "retail" => ProductType.Retail,
            _ => (ProductType?)null,
        };
        if (!type.HasValue) return Invalid(ProductImportErrors.ProductType);
        if (!Guid.TryParse(row.CategoryId, out var categoryId) || !activeCategories.Contains(categoryId))
            return Invalid(ProductImportErrors.Category);
        if (!TryExactDecimal(row.RetailPrice, out var retail)) return Invalid(ProductImportErrors.Price);
        decimal? wholesale = null;
        if (!string.IsNullOrWhiteSpace(row.WholesalePrice))
        {
            if (!TryExactDecimal(row.WholesalePrice, out var value)) return Invalid(ProductImportErrors.Price);
            wholesale = value;
        }
        var active = true;
        if (!string.IsNullOrWhiteSpace(row.IsActive) && !bool.TryParse(row.IsActive.Trim(), out active))
            return Invalid(ProductImportErrors.Active);
        MedicineData? medicine = null;
        if (type == ProductType.Retail)
        {
            if (!string.IsNullOrWhiteSpace(row.ActiveIngredients) || !string.IsNullOrWhiteSpace(row.Strengths) ||
                !string.IsNullOrWhiteSpace(row.DosageForm) || !string.IsNullOrWhiteSpace(row.Route) ||
                !string.IsNullOrWhiteSpace(row.SanitaryRegistration))
                return Invalid(ProductImportErrors.RetailMedicine);
        }
        else
        {
            var ingredients = row.ActiveIngredients.Split(ProductImportTemplate.ComponentSeparator, StringSplitOptions.TrimEntries);
            var strengths = row.Strengths.Split(ProductImportTemplate.ComponentSeparator, StringSplitOptions.TrimEntries);
            if (ingredients.Length != strengths.Length || ingredients.Any(string.IsNullOrWhiteSpace) || strengths.Any(string.IsNullOrWhiteSpace))
                return Invalid(ProductImportErrors.Components);
            try
            {
                medicine = new MedicineInput(ingredients.Select((value, index) => new MedicineComponentInput(value, strengths[index])).ToArray(),
                    row.DosageForm, row.Route, row.SanitaryRegistration).ToData();
            }
            catch (ArgumentException) { return Invalid(ProductImportErrors.Medicine); }
        }
        BusinessProduct product;
        try
        {
            product = BusinessProduct.CreateLocal(tenantId, code, type.Value, row.Name, categoryId, row.BrandOrLaboratory,
                row.Barcode, medicine, retail, wholesale, now, active);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.ParamName switch
            {
                "internalCode" => ProductImportErrors.Code,
                "name" => ProductImportErrors.Name,
                "brandOrLaboratory" => ProductImportErrors.Brand,
                "barcode" => ProductImportErrors.Barcode,
                "value" => ProductImportErrors.Price,
                _ => ProductImportErrors.Row,
            });
        }
        try
        {
            var unit = ProductUnit.Create(tenantId, product.Id, product.TenantId, row.BaseUnitName, 1m, true);
            return new(row.RowNumber, product, unit, null);
        }
        catch (ArgumentException) { return Invalid(ProductImportErrors.BaseUnit); }
    }

    private static bool TryExactDecimal(string source, out decimal value)
    {
        var text = source?.Trim() ?? string.Empty;
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
        if (!decimal.TryParse(text, style, CultureInfo.InvariantCulture, out value)) return false;
        // decimal.TryParse can round overly precise literals. Compare the original rational value exactly before Domain validates price.
        var exponentPosition = text.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (exponentPosition >= 0 && (!int.TryParse(text[(exponentPosition + 1)..], NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out exponent) || exponent < -128 || exponent > 128)) return false;
        var significand = exponentPosition < 0 ? text : text[..exponentPosition];
        var negative = significand.StartsWith('-');
        significand = significand.TrimStart('+', '-');
        var point = significand.IndexOf('.', StringComparison.Ordinal);
        var scale = (point < 0 ? 0 : significand.Length - point - 1) - exponent;
        var digits = significand.Replace(".", string.Empty, StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return value == 0;
        while (digits.EndsWith('0')) { digits = digits[..^1]; scale--; }
        if (digits.Length > 128 || scale < -128 || scale > 128) return false;
        var original = BigInteger.Parse(digits, CultureInfo.InvariantCulture) * (negative ? -1 : 1);
        var bits = decimal.GetBits(value);
        var parsed = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
        if (bits[3] < 0) parsed = -parsed;
        var parsedScale = (bits[3] >> 16) & 0xff;
        return scale >= 0
            ? original * BigInteger.Pow(10, parsedScale) == parsed * BigInteger.Pow(10, scale)
            : original * BigInteger.Pow(10, parsedScale - scale) == parsed;
    }
}
