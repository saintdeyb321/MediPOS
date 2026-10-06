namespace MediPOS.Application.Modules.Branches.CreateLegalEntity;

// ActorId is supplied by an authenticated server caller, never a freely bound HTTP field.
public sealed record CreateLegalEntityCommand(Guid TenantId, string LegalName, string Ruc, Guid ActorId);
