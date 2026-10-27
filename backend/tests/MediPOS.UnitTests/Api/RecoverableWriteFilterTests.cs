using System.Text;
using MediPOS.Api.Errors;
using MediPOS.Api.Tenant;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MediPOS.UnitTests.Api;

public sealed class RecoverableWriteFilterTests
{
    [Theory]
    [InlineData("supplier", 201)]
    [InlineData("sale-draft", 201)]
    [InlineData("product-import", 201)]
    [InlineData("minimum", 200)]
    [InlineData("adjustment", 200)]
    public async Task ReplaysKeepTheirOfficialStatusWithoutCallingTheMutation(string kind, int status)
    {
        var operations = new Operations();
        using var services = new ServiceCollection().AddSingleton<IRecoverableOperations>(operations).BuildServiceProvider();
        var http = Context(services);
        var originalBody = http.Request.Body;
        var called = false;
        var result = await new RecoverableWriteFilter(kind).InvokeAsync(new DefaultEndpointFilterInvocationContext(http, []), _ => { called = true; throw new InvalidOperationException("Replay must not invoke the mutation."); });
        Assert.False(called);
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(Guid.Parse(http.Request.RouteValues["branchId"]!.ToString()!), operations.Branch);
        Assert.Equal(64, operations.Hash!.Length);
        Assert.Same(originalBody, http.Request.Body);
    }

    [Fact]
    public async Task OversizedRequestIsRejectedBeforeAnyCoordinatorCall()
    {
        var operations = new Operations();
        using var services = new ServiceCollection().AddSingleton<IRecoverableOperations>(operations).BuildServiceProvider();
        var http = Context(services); http.Request.Body = new MemoryStream(new byte[TenantHttp.MaximumBodyBytes + 1]);
        var error = await Assert.ThrowsAsync<HttpBoundaryException>(async () => await new RecoverableWriteFilter("supplier").InvokeAsync(new DefaultEndpointFilterInvocationContext(http, []), _ => throw new InvalidOperationException()));
        Assert.Equal(413, error.StatusCode); Assert.Null(operations.Hash);
    }

    private static DefaultHttpContext Context(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.RouteValues["tenantId"] = Guid.NewGuid().ToString("D");
        context.Request.RouteValues["branchId"] = Guid.NewGuid().ToString("D");
        context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("D");
        context.Request.Method = "POST"; context.Request.Path = "/api/tenants/test/suppliers";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        return context;
    }
    private sealed class Operations : IRecoverableOperations
    {
        public Guid Branch { get; private set; }
        public string? Hash { get; private set; }
        public Task<OperationReceipt> ExecuteAsync(Guid tenantId, Guid branchId, Guid requestId, string kind, string payloadHash, Func<CancellationToken, Task<string>> execute, CancellationToken token)
        {
            Branch = branchId; Hash = payloadHash;
            return Task.FromResult(new OperationReceipt(requestId, kind, """{"value":1.25}"""));
        }
        public Task<OperationReceipt> ReadAsync(Guid tenantId, Guid branchId, Guid requestId, CancellationToken token) => throw new NotSupportedException();
    }
}
