using MediPOS.Domain.Modules.Branches;

namespace MediPOS.Application.Modules.Branches;

public sealed record LegalEntityDetails(Guid Id, Guid TenantId, string LegalName, string Ruc, DateTimeOffset CreatedAt)
{
    internal static LegalEntityDetails From(LegalEntity legalEntity) =>
        new(legalEntity.Id, legalEntity.TenantId, legalEntity.LegalName, legalEntity.Ruc, legalEntity.CreatedAt);
}
