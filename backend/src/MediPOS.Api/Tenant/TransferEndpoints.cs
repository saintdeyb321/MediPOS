using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediPOS.Api.Errors;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Transfers;
using MediPOS.Application.Modules.Transfers.ApproveTransfer;
using MediPOS.Application.Modules.Transfers.CancelTransfer;
using MediPOS.Application.Modules.Transfers.DispatchTransfer;
using MediPOS.Application.Modules.Transfers.GetTransfer;
using MediPOS.Application.Modules.Transfers.ReceiveTransfer;
using MediPOS.Application.Modules.Transfers.RequestTransfer;
using MediPOS.Domain.Modules.Transfers;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapTransfers(RouteGroupBuilder tenants)
    {
        tenants.MapGet("/transfers", async (HttpContext c, GetTransfersHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId"), TenantHttp.Query(c, "status") is null ? null : TenantHttp.Choice(c, "status", TransferStatus.Requested), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<IReadOnlyList<TransferSummary>>().WithName("tenant.transfers.list").WithQuery<TenantOpenApi.Transfers>();
        tenants.MapPost("/transfers", RequestGoods)
            .Accepts<TransferRequest>("application/json").Produces<TransferDetails>(201).WithName("tenant.transfers.request");
        tenants.MapGet("/transfers/{transferId}", async (HttpContext c, GetTransferHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "transferId")), c.RequestAborted)))
            .Produces<TransferDetails>().WithName("tenant.transfers.read");
        tenants.MapPost("/transfers/{transferId}/approve", async (HttpContext c, ApproveTransferHandler handler) =>
        {
            await TenantHttp.Read<EmptyRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "transferId")), c.RequestAborted));
        }).Accepts<EmptyRequest>("application/json").Produces<TransferDetails>().WithName("tenant.transfers.approve");
        tenants.MapPost("/transfers/{transferId}/cancel", async (HttpContext c, CancelTransferHandler handler) =>
        {
            var input = await TenantHttp.Read<CancelTransferRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "transferId"), input.Reason), c.RequestAborted));
        }).Accepts<CancelTransferRequest>("application/json").Produces<TransferDetails>().WithName("tenant.transfers.cancel");
        tenants.MapPost("/transfers/{transferId}/dispatch", async (HttpContext c, DispatchTransferHandler handler) =>
        {
            var input = await TenantHttp.Read<DispatchTransferRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "transferId"), input.Allocations), c.RequestAborted));
        }).Accepts<DispatchTransferRequest>("application/json").Produces<TransferDetails>().WithMetadata(new TenantRequestPolicy(256 * 1024, false)).WithName("tenant.transfers.dispatch");
        tenants.MapPost("/transfers/{transferId}/receive", async (HttpContext c, ReceiveTransferHandler handler) =>
        {
            var input = await TenantHttp.Read<ReceiveTransferRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "transferId"), input.Quantities), c.RequestAborted));
        }).Accepts<ReceiveTransferRequest>("application/json").Produces<TransferDetails>().WithMetadata(new TenantRequestPolicy(256 * 1024, false)).WithName("tenant.transfers.receive");
    }
    private static async Task<IResult> RequestGoods(HttpContext context, RequestTransferHandler handler)
    {
        var input = await TenantHttp.Read<TransferRequest>(context);
        var command = new RequestTransferCommand(TenantHttp.Tenant(context), input.SourceBranchId, input.DestinationBranchId, input.Lines);
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keys))
            return TenantHttp.Response(await handler.HandleAsync(command, context.RequestAborted), 201);
        if (keys.Count != 1) throw new HttpBoundaryException(400, "api.invalid_identifier");
        var requestId = TenantHttp.ParseId(keys[0]);
        if (context.Request.Headers.TryGetValue("X-MediPOS-Branch", out var branch) &&
            (branch.Count != 1 || TenantHttp.ParseId(branch[0]) != command.DestinationBranchId))
            throw new HttpBoundaryException(403, "access.branch_denied");

        // Use the strict DTO's destination before replay or authorization. The header cannot select a different branch.
        var payloadHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input, TenantHttp.Json)));
        var receipt = await context.RequestServices.GetRequiredService<IRecoverableOperations>()
            .ExecuteAsync(command.TenantId, command.DestinationBranchId, requestId, "transfer-request", payloadHash,
                async token => JsonSerializer.Serialize(await handler.HandleAsync(command, token), TenantHttp.Json), context.RequestAborted);
        return Results.Text(receipt.ResultJson, "application/json", statusCode: 201);
    }
    public sealed record TransferRequest([property: JsonRequired] Guid SourceBranchId, [property: JsonRequired] Guid DestinationBranchId, [property: JsonRequired] IReadOnlyList<TransferLineInput> Lines);
    public sealed record CancelTransferRequest([property: JsonRequired] string Reason);
    public sealed record DispatchTransferRequest([property: JsonRequired] IReadOnlyList<TransferDispatchSelection> Allocations);
    public sealed record ReceiveTransferRequest([property: JsonRequired] IReadOnlyList<TransferReceiptSelection> Quantities);
}
