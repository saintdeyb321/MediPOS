using System.Globalization;
using MediPOS.Domain.Modules.Catalog;

namespace MediPOS.Performance;

public sealed record SyntheticProduct(int Index, string Code, string Name, string Brand, string Barcode,
    ProductType Type, string? Ingredient, string? Strength, decimal Price);

public static class SyntheticCatalog
{
    // Content/distribution are deterministic, independent of thread scheduling and Random implementation.
    // Domain factories retain UUIDv7 generation; generated record IDs are deliberately not overwritten.
    public static SyntheticProduct Product(int seed, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var bucket = (int)((unchecked((uint)seed * 2654435761U) + (uint)index * 2246822519U) % 7);
        var medicine = index % 2 == 0;
        var name = index switch
        {
            0 => "Microgénico sin stock",
            2 => "Equivalente sintético",
            4 => "Ácido Sintético 500 mg",
            6 => "Medicamento otra sede",
            _ => medicine ? "Medicamento sintético " + (index / 2).ToString("D5", CultureInfo.InvariantCulture) :
                "Retail sintético " + (index / 2).ToString("D5", CultureInfo.InvariantCulture),
        };
        return new(index, "P" + index.ToString("D6", CultureInfo.InvariantCulture), name,
            "Laboratorio Ñandú " + bucket.ToString(CultureInfo.InvariantCulture), "290" + index.ToString("D10", CultureInfo.InvariantCulture),
            medicine ? ProductType.Medicine : ProductType.Retail, medicine ? (index is 0 or 2 ? "Ingrediente equivalente" : "Ingrediente sintético " + bucket.ToString(CultureInfo.InvariantCulture)) : null,
            medicine ? (index is 0 or 2 ? "25 µg" : "500 mg") : null, 2m + bucket);
    }
}
