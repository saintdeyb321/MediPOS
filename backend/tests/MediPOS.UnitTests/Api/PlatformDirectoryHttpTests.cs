using System.Net;
using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.Catalog;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.TenancyLicensing;
using MediPOS.Domain.Modules.TenancyLicensing;
using Microsoft.Extensions.DependencyInjection;
using GoogleHost = MediPOS.UnitTests.Api.GoogleAuthenticationHttpTests.Host;

namespace MediPOS.UnitTests.Api;

// Real local HTTP/session/origin boundary; provider and reader are unit doubles, not PostgreSQL/E2E evidence.
[Collection("Observability")]
public sealed class PlatformDirectoryHttpTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly string[] SummaryFields = ["tenantId", "tradingName", "createdAt"];

    [Theory]
    [InlineData("/platform/session")]
    [InlineData("/platform/catalog/categories?limit=100")]
    [InlineData("/platform/tenants?limit=20")]
    [InlineData("/platform/tenants/019a0162-0000-7000-8000-000000000002/details")]
    [InlineData("/platform/identities/019a0162-0000-7000-8000-000000000002")]
    [InlineData("/platform/tenant-requests/019a0162-0000-7000-8000-000000000002")]
    public async Task AnonymousAndOrdinaryGoogleSessionsCannotReadPlatform(string path)
    {
        var reader = new Reader(); await using var host = await Host(reader, approved: false);
        using var anonymous = await Get(host, path); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await PlatformLicenseHttpTests.Login(host);
        using var ordinary = await Get(host, path); Assert.Equal(HttpStatusCode.Forbidden, ordinary.StatusCode);
        Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?limit=1&limit=2")]
    [InlineData("?role=owner")]
    [InlineData("?actorId=arbitrary")]
    [InlineData("?afterTenantId=bad")]
    public async Task InvalidOrUnboundedQueriesNeverReadPersistence(string query)
    {
        var reader = new Reader(); await using var host = await Host(reader); await PlatformLicenseHttpTests.Login(host);
        using var response = await Get(host, "/platform/tenants" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task ApprovedReadsExposeOnlyBoundedDirectoryLicenseAndExactRegisteredIdentity()
    {
        var reader = new Reader(); await using var host = await Host(reader); await PlatformLicenseHttpTests.Login(host);
        using var response = await Get(host, "/platform/tenants?limit=2"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(2, reader.Limit); Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(SummaryFields, page.RootElement.GetProperty("items")[0].EnumerateObject().Select(p => p.Name));
        using var license = await Get(host, $"/platform/tenants/{reader.Tenant:D}/details"); Assert.Equal(HttpStatusCode.OK, license.StatusCode);
        using var data = JsonDocument.Parse(await license.Content.ReadAsStringAsync(Token));
        Assert.Equal("trial", data.RootElement.GetProperty("license").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("initialOwnerUserId").ValueKind);
        using var identity = await Get(host, $"/platform/identities/{reader.User:D}"); Assert.Equal(HttpStatusCode.OK, identity.StatusCode);
        using var user = JsonDocument.Parse(await identity.Content.ReadAsStringAsync(Token));
        Assert.False(user.RootElement.TryGetProperty("googleSubject", out _)); Assert.Equal(3, user.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task ReceiptReadUsesServerActorAndMissingResultDoesNotReserveOrWrite()
    {
        var reader = new Reader(); await using var host = await Host(reader); await PlatformLicenseHttpTests.Login(host);
        using var response = await Get(host, $"/platform/tenant-requests/{Guid.NewGuid():D}"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(reader.Operator.Id, reader.ReceiptActor);
        reader.Missing = true;
        using var missing = await Get(host, $"/platform/tenant-requests/{Guid.NewGuid():D}"); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var identity = await Get(host, $"/platform/identities/{Guid.NewGuid():D}"); Assert.Equal(HttpStatusCode.NotFound, identity.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadOriginAndDisabledSurfaceStayClosed(bool disabled)
    {
        var reader = new Reader(); await using var host = await Host(reader, enabled: !disabled); await PlatformLicenseHttpTests.Login(host);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/platform/tenants");
        request.Headers.Add("Origin", "https://foreign.example.invalid");
        using var response = await host.Client.SendAsync(request, Token);
        Assert.Equal(disabled ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task AuthorityRevocationAfterReadCannotExposeItsResult()
    {
        var reader = new Reader(); var authority = new Authority(reader.Operator.Id);
        var queries = new PlatformDirectoryQueries(reader, authority);
        var failure = await Assert.ThrowsAsync<ApplicationErrorException>(() => queries.TenantsAsync(null, 20, Token));
        Assert.Equal("access.platform_denied", failure.Error.Code); Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task CategoriesAreBoundedGlobalReferenceMetadataAndNeverCommercialProducts()
    {
        var reader = new Reader(); var categories = new Categories();
        await using var host = await Host(reader, categories: categories); await PlatformLicenseHttpTests.Login(host);
        using var invalid = await Get(host, "/platform/catalog/categories?limit=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.Equal(0, categories.Limit);
        using var duplicate = await Get(host, "/platform/catalog/categories?limit=1&limit=2");
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode); Assert.Equal(0, categories.Limit);
        using var result = await Get(host, "/platform/catalog/categories?limit=2");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode); Assert.True(result.Headers.CacheControl?.NoStore);
        Assert.Equal(2, categories.Limit);
        using var data = JsonDocument.Parse(await result.Content.ReadAsStringAsync(Token));
        Assert.Single(data.RootElement.EnumerateArray());
        Assert.Equal("Synthetic category", data.RootElement[0].GetProperty("name").GetString());
        var denied = await Assert.ThrowsAsync<ApplicationErrorException>(() =>
            new PlatformCategoryQueries(categories, new Authority(reader.Operator.Id)).ReadAsync(20, Token));
        Assert.Equal("access.platform_denied", denied.Error.Code);
    }

    private static Task<GoogleHost> Host(Reader reader, bool approved = true, bool enabled = true, Categories? categories = null) => GoogleHost.CreateAsync(
        configure: config =>
        {
            config["PlatformAdministration:Enabled"] = "true"; config["PlatformAdministration:AdministratorUserIds:0"] = (approved ? reader.Operator.Id : Guid.NewGuid()).ToString("D");
            config["PlatformApi:Enabled"] = enabled ? "true" : "false";
        }, seedUsers: users => users.BySubject[reader.Operator.GoogleSubject] = reader.Operator,
        configureServices: services =>
        {
            services.AddScoped<IPlatformDirectoryReader>(_ => reader);
            services.AddScoped<ICatalogDirectoryReader>(_ => categories ?? new Categories());
        });
    private static async Task<HttpResponseMessage> Get(GoogleHost host, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path); request.Headers.Add("Origin", "https://medipos.example.test");
        return await host.Client.SendAsync(request, Token);
    }
    private sealed class Authority(Guid actor) : IPlatformAdministration
    {
        private int _calls;
        public Task<Guid?> GetAuthorizedActorAsync(CancellationToken token) => Task.FromResult<Guid?>(++_calls == 1 ? actor : null);
    }
    private sealed class Reader : IPlatformDirectoryReader
    {
        internal MediPOS.Domain.Modules.IdentityAccess.User Operator { get; } = PlatformLicenseHttpTests.Operator();
        internal Guid Tenant { get; } = Guid.NewGuid(); internal Guid User { get; } = Guid.NewGuid();
        internal int Calls, Limit; internal Guid ReceiptActor; internal bool Missing;
        private LicenseDetails License => new(Tenant, Guid.NewGuid(), LicenseStatus.Trial, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), 2);
        public Task<PlatformTenantPage> TenantsAsync(Guid? afterTenantId, int limit, CancellationToken token)
        { Calls++; Limit = limit; return Task.FromResult(new PlatformTenantPage([new(Tenant, "Synthetic QA tenant", DateTimeOffset.UtcNow)], null)); }
        public Task<PlatformTenantDetails?> TenantAsync(Guid tenantId, CancellationToken token)
        { Calls++; return Task.FromResult<PlatformTenantDetails?>(new(new(Tenant, "Synthetic QA tenant", DateTimeOffset.UtcNow), License, null)); }
        public Task<PlatformGoogleIdentity?> GoogleIdentityAsync(Guid userId, CancellationToken token)
        { Calls++; return Task.FromResult<PlatformGoogleIdentity?>(Missing ? null : new(User, "Synthetic registered user", "qa@example.invalid")); }
        public Task<LicenseDetails?> CreationResultAsync(Guid actorId, Guid requestKey, CancellationToken token)
        { Calls++; ReceiptActor = actorId; return Task.FromResult<LicenseDetails?>(Missing ? null : License); }
    }
    private sealed class Categories : ICatalogDirectoryReader
    {
        internal int Limit;
        public Task<IReadOnlyList<CategoryDetails>> CategoriesAsync(int offset, int limit, CancellationToken token)
        { Assert.Equal(0, offset); Limit = limit; return Task.FromResult<IReadOnlyList<CategoryDetails>>([new(Guid.NewGuid(), "Synthetic category", true, DateTimeOffset.UtcNow)]); }
        public Task<IReadOnlyList<GlobalProductDetails>> GlobalProductsAsync(int offset, int limit, CancellationToken token) => throw new InvalidOperationException("Not a product directory.");
        public Task<IReadOnlyList<BusinessProductDetails>> ProductsAsync(Guid tenantId, int offset, int limit, CancellationToken token) => throw new InvalidOperationException("No tenant access.");
        public Task<CatalogProductView?> ProductAsync(Guid tenantId, Guid productId, CancellationToken token) => throw new InvalidOperationException("No tenant access.");
    }
}
