namespace MediPOS.Application.Modules.Branches.CreateBranch;

public sealed record CreateBranchCommand(Guid TenantId, Guid LegalEntityId, string Name);
