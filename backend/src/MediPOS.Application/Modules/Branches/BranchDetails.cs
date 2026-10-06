using MediPOS.Domain.Modules.Branches;

namespace MediPOS.Application.Modules.Branches;

public sealed record BranchDetails(Guid Id, Guid TenantId, Guid LegalEntityId, string Name, bool IsMainHub, DateTimeOffset CreatedAt)
{
    internal static BranchDetails From(Branch branch) =>
        new(branch.Id, branch.TenantId, branch.LegalEntityId, branch.Name, branch.IsMainHub, branch.CreatedAt);
}
