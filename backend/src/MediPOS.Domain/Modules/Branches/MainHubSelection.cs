namespace MediPOS.Domain.Modules.Branches;

public static class MainHubSelection
{
    public static void Move(Branch? currentHub, Branch selectedBranch)
    {
        ArgumentNullException.ThrowIfNull(selectedBranch);

        if (currentHub is not null && currentHub.TenantId != selectedBranch.TenantId)
        {
            throw new ArgumentException("Both hub branches must belong to the same tenant.", nameof(selectedBranch));
        }

        currentHub?.ClearMainHub();
        selectedBranch.MarkAsMainHub();
    }
}
