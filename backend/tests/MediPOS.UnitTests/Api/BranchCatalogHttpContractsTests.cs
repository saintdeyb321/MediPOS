using System.Text.Json;
using MediPOS.Api.Modules.OfflineSync;
using MediPOS.Application.Errors;
using MediPOS.Application.Tenancy;

namespace MediPOS.UnitTests.Api;

public sealed class BranchCatalogHttpContractsTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    [Theory]
    [InlineData("tenantId")]
    [InlineData("branchId")]
    [InlineData("membershipId")]
    [InlineData("actorId")]
    [InlineData("role")]
    public void HttpDoesNotAcceptClientAuthorityFields(string field) => Assert.Throws<JsonException>(() =>
        JsonSerializer.Deserialize<BranchCatalogRequest>("{\"deviceId\":\"" + Guid.NewGuid() + "\",\"" + field + "\":\"untrusted\"}", Options));
    [Fact]
    public void MappingRequiresServerTenantAndLeavesBranchResolutionToDeviceAuthorization()
    {
        var context = new TenantDataContext(); var request = new BranchCatalogRequest(Guid.NewGuid());
        Assert.Throws<ApplicationErrorException>(() => BranchCatalogHttpMapping.Snapshot(request, context));
        var tenant = Guid.NewGuid(); context.SelectTenant(tenant);
        var snapshot = BranchCatalogHttpMapping.Snapshot(request, context); var changes = BranchCatalogHttpMapping.Changes(request, context);
        Assert.Equal(tenant, snapshot.TenantId); Assert.Equal(tenant, changes.TenantId); Assert.Null(snapshot.BranchId); Assert.Equal(100, snapshot.PageSize);
    }
}
