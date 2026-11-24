using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediPOS.Api.Errors;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.DispatchCashTransfer;
using MediPOS.Application.Modules.Cash.GetCashTransfer;
using MediPOS.Application.Modules.Cash.GetPendingCashTransfers;
using MediPOS.Application.Modules.Cash.ReceiveCashTransfer;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapCashTransfers(RouteGroupBuilder tenants)
    {
        tenants.MapPost("/branches/{branchId}/cash-transfers", DispatchCashTransfer)
            .Accepts<DispatchCashTransferRequest>("application/json").Produces<CashTransferDetails>(201).WithName("tenant.cash_transfers.dispatch");
        tenants.MapGet("/cash-transfers/pending", async (HttpContext c, GetPendingCashTransfersHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "destinationBranchId"), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<PendingCashTransfers>().WithName("tenant.cash_transfers.pending").WithQuery<TenantOpenApi.CashTransfers>();
        tenants.MapGet("/cash-transfers/{cashTransferId}", async (HttpContext c, GetCashTransferHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "cashTransferId")), c.RequestAborted)))
            .Produces<CashTransferDetails>().WithName("tenant.cash_transfers.read");
        tenants.MapPost("/cash-transfers/{cashTransferId}/receive", async (HttpContext c, ReceiveCashTransferHandler handler) =>
        {
            var input = await TenantHttp.Read<ReceiveCashTransferRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Id(c, "cashTransferId"), input.DestinationCashSessionId), c.RequestAborted));
        }).Accepts<ReceiveCashTransferRequest>("application/json").Produces<CashTransferDetails>().WithName("tenant.cash_transfers.receive");
    }

    private static async Task<IResult> DispatchCashTransfer(HttpContext context, DispatchCashTransferHandler handler)
    {
        var input = await TenantHttp.Read<DispatchCashTransferRequest>(context);
        var command = new DispatchCashTransferCommand(TenantHttp.Tenant(context), TenantHttp.Branch(context), input.SourceCashSessionId, input.DestinationBranchId, input.Amount);
        if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keys))
            return TenantHttp.Response(await handler.HandleAsync(command, context.RequestAborted), 201);
        if (keys.Count != 1) throw new HttpBoundaryException(400, "api.invalid_identifier");
        var requestId = TenantHttp.ParseId(keys[0]);
        if (context.Request.Headers.TryGetValue("X-MediPOS-Branch", out var branch) &&
            (branch.Count != 1 || TenantHttp.ParseId(branch[0]) != command.SourceBranchId))
            throw new HttpBoundaryException(403, "access.branch_denied");

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            input.SourceCashSessionId,
            input.DestinationBranchId,
            Amount = input.Amount.ToString("G29", CultureInfo.InvariantCulture),
        }, TenantHttp.Json);
        var receipt = await context.RequestServices.GetRequiredService<IRecoverableOperations>()
            .ExecuteAsync(command.TenantId, command.SourceBranchId, requestId, DispatchCashTransferHandler.ReceiptKind, Convert.ToHexString(SHA256.HashData(payload)),
                async token => JsonSerializer.Serialize(await handler.HandleAsync(command, token), TenantHttp.Json), context.RequestAborted);
        return Results.Text(receipt.ResultJson, "application/json", statusCode: 201);
    }

    public sealed record DispatchCashTransferRequest([property: JsonRequired] Guid SourceCashSessionId, [property: JsonRequired] Guid DestinationBranchId, [property: JsonRequired] decimal Amount);
    public sealed record ReceiveCashTransferRequest([property: JsonRequired] Guid DestinationCashSessionId);
}
