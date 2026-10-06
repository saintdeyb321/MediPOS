namespace MediPOS.Domain.Modules.IdentityAccess;

public sealed class MembershipBranch
{
    private MembershipBranch() { }

    public Guid TenantId { get; private set; }
    public Guid MembershipId { get; private set; }
    public Guid BranchId { get; private set; }

    public static MembershipBranch Create(
        Guid tenantId, Guid membershipId, Guid membershipTenantId, Guid branchId, Guid branchTenantId)
    {
        if (tenantId == Guid.Empty || membershipId == Guid.Empty || branchId == Guid.Empty)
            throw new ArgumentException("Tenant, membership and branch identifiers are required.");
        if (membershipTenantId != tenantId || branchTenantId != tenantId)
            throw new ArgumentException("Membership and branch must belong to the same tenant.");
        return new MembershipBranch { TenantId = tenantId, MembershipId = membershipId, BranchId = branchId };
    }
}
