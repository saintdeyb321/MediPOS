namespace MediPOS.Application.Modules.Branches.CreateBranch;

// ActorId is supplied by an authenticated server caller, never a freely bound HTTP field.
public sealed record CreateBranchCommand(Guid TenantId, Guid LegalEntityId, string Name, Guid ActorId);
