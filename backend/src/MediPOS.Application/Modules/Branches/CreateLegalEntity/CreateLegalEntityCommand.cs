namespace MediPOS.Application.Modules.Branches.CreateLegalEntity;

public sealed record CreateLegalEntityCommand(Guid TenantId, string LegalName, string Ruc);
