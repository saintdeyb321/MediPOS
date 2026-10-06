namespace MediPOS.Application.Modules.Branches.SetMainHubBranch;

// ActorId is supplied by an authenticated server caller, never a freely bound HTTP field.
public sealed record SetMainHubBranchCommand(Guid TenantId, Guid BranchId, Guid ActorId);
