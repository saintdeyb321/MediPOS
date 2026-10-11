using System.Diagnostics.CodeAnalysis;
using MediPOS.Domain.Modules.Catalog;
using MediPOS.Domain.Modules.Catalog.OfficialCuration;
using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialCuration;

public sealed record OfficialCatalogMatchProjection(GlobalProduct Product, OfficialCatalogPublication? Origin, MedicineProfile? Medicine);
public static class OfficialCatalogCurationQueries
{
    [SuppressMessage("Globalization", "CA1304", Justification = "Npgsql translates this expression to indexed PostgreSQL UPPER, not a CLR culture operation.")]
    [SuppressMessage("Globalization", "CA1311", Justification = "Npgsql translates this expression to indexed PostgreSQL UPPER, not a CLR culture operation.")]
    [SuppressMessage("Performance", "CA1862", Justification = "StringComparison is not translated by EF; approximate database screening is verified with invariant domain evidence.")]
    public static IQueryable<OfficialCatalogMatchProjection> Matches(IQueryable<GlobalProduct> globals, IQueryable<MedicineProfile> profiles,
        IQueryable<OfficialCatalogPublication> publications, string source, string? identity, string name, string registration,
        string? fingerprint, string? equivalence, int offset, int take)
    {
        if (offset < 0 || take is < 1 or > 101) throw new ArgumentOutOfRangeException(nameof(take));
        var origins = publications.AsNoTracking(); var medicines = profiles.AsNoTracking();
        var products = globals.AsNoTracking().Where(product =>
            origins.Any(origin => origin.GlobalProductId == product.Id && ((origin.SourceCode == source && origin.OfficialIdentity == identity) || origin.CandidateFingerprint == fingerprint)) ||
            product.Name.Trim().ToUpper() == name ||
            medicines.Any(medicine => medicine.GlobalProductId == product.Id &&
                ((equivalence != null && medicine.Data.EquivalenceKey == equivalence) || (registration != "" && medicine.Data.SanitaryRegistration != null && medicine.Data.SanitaryRegistration.Trim().ToUpper() == registration))));
        return products.OrderBy(product => origins.Any(origin => origin.GlobalProductId == product.Id && origin.SourceCode == source && origin.OfficialIdentity == identity) ? 0 :
                origins.Any(origin => origin.GlobalProductId == product.Id && origin.CandidateFingerprint == fingerprint) ? 1 : 2)
            .ThenBy(product => product.Id).Skip(offset).Take(take)
            .Select(product => new OfficialCatalogMatchProjection(product,
                origins.Where(origin => origin.GlobalProductId == product.Id).OrderBy(origin => origin.SourceCode == source && origin.OfficialIdentity == identity ? 0 : origin.CandidateFingerprint == fingerprint ? 1 : 2).ThenBy(origin => origin.Id).FirstOrDefault(),
                medicines.FirstOrDefault(medicine => medicine.GlobalProductId == product.Id)));
    }
}
