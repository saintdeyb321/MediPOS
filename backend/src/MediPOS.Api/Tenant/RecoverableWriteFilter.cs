using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediPOS.Api.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MediPOS.Api.Tenant;

// Opt-in compatibility: existing unkeyed internal callers keep their contracts; the UI always supplies an intention.
public sealed class RecoverableWriteFilter(string kind) : IEndpointFilter
{
    internal static int ReplayStatus(string operationKind) => operationKind switch
    {
        "supplier" or "sale-draft" or "product-import" => StatusCodes.Status201Created,
        "adjustment" or "minimum" => StatusCodes.Status200OK,
        _ => throw new InvalidOperationException("Unsupported recoverable operation."),
    };

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var c = context.HttpContext;
        if (!c.Request.Headers.TryGetValue("Idempotency-Key", out var keys)) return await next(context);
        if (keys.Count != 1) throw new HttpBoundaryException(400, "api.invalid_identifier");
        var requestId = TenantHttp.ParseId(keys[0]);
        var branchId = c.Request.RouteValues.ContainsKey("branchId") ? TenantHttp.Branch(c) : TenantHttp.ParseId(c.Request.Headers["X-MediPOS-Branch"].ToString());
        var maximum = kind == "product-import" ? 5 * 1024 * 1024 : TenantHttp.MaximumBodyBytes;
        var buffer = new byte[maximum + 1];
        var count = await c.Request.Body.ReadAtLeastAsync(buffer, buffer.Length, false, c.RequestAborted);
        if (count > maximum) throw new HttpBoundaryException(413, "api.body_too_large");
        var suffix = Encoding.UTF8.GetBytes(c.Request.Method + " " + c.Request.Path + c.Request.QueryString);
        // Use a single bounded copy; never log the body or retain uploaded files outside request lifetime.
        var payloadHash = Convert.ToHexString(SHA256.HashData(suffix.Concat(buffer.Take(count)).ToArray()));
        var originalBody = c.Request.Body;
        using var body = new MemoryStream(buffer, 0, count, writable: false); c.Request.Body = body;
        object? response = null;
        try
        {
            var receipt = await c.RequestServices.GetRequiredService<IRecoverableOperations>().ExecuteAsync(TenantHttp.Tenant(c), branchId, requestId, kind, payloadHash,
                async token =>
                {
                    if (kind == "adjustment")
                    {
                        var actualBranch = await c.RequestServices.GetRequiredService<MediPOS.Application.Modules.Inventory.IStockAdjustmentTransaction>()
                            .FindLotBranchAsync(TenantHttp.Tenant(c), TenantHttp.Id(c, "lotId"), token);
                        if (actualBranch != branchId) throw new HttpBoundaryException(403, "access.branch_denied");
                    }
                    response = await next(context);
                    if (response is not IValueHttpResult value || response is IStatusCodeHttpResult status && status.StatusCode >= 400)
                        throw new InvalidOperationException("Recoverable writes require a successful typed JSON result.");
                    return JsonSerializer.Serialize(value.Value, TenantHttp.Json);
                }, c.RequestAborted);
            return response ?? Results.Text(receipt.ResultJson, "application/json", statusCode: ReplayStatus(kind));
        }
        finally { c.Request.Body = originalBody; }
    }
}
