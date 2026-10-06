using MediPOS.Domain.Modules.AuditSupport;
using MediPOS.Domain.Modules.Branches;

namespace MediPOS.Application.Modules.Branches;

public interface IBranchesStore
{
    Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<LegalEntity?> FindLegalEntityAsync(Guid tenantId, Guid legalEntityId, CancellationToken cancellationToken);
    Task<Branch?> FindBranchAsync(Guid tenantId, Guid branchId, CancellationToken cancellationToken);
    Task<Branch?> FindMainHubBranchAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<int> CountBranchesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task AddLegalEntityAsync(LegalEntity legalEntity, AuditLog audit, CancellationToken cancellationToken);
    Task AddBranchAsync(Branch branch, AuditLog audit, CancellationToken cancellationToken);
    Task ReplaceMainHubAsync(Branch branch, AuditLog audit, CancellationToken cancellationToken);
}
