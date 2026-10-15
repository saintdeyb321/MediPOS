using System.Text.Json.Serialization;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.DispatchCashTransfer;
using MediPOS.Application.Modules.Cash.GetCashTransfer;
using MediPOS.Application.Modules.Cash.GetPendingCashTransfers;
using MediPOS.Application.Modules.Cash.ReceiveCashTransfer;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapCashTransfers(RouteGroupBuilder tenants)
    {
        tenants.MapPost("/branches/{branchId}/cash-transfers", async (HttpContext c, DispatchCashTransferHandler handler) =>
        {
            var input = await TenantHttp.Read<DispatchCashTransferRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), input.SourceCashSessionId, input.DestinationBranchId, input.Amount), c.RequestAborted), 201);
        }).Accepts<DispatchCashTransferRequest>("application/json").Produces<CashTransferDetails>(201).WithName("tenant.cash_transfers.dispatch");
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
    public sealed record DispatchCashTransferRequest([property: JsonRequired] Guid SourceCashSessionId, [property: JsonRequired] Guid DestinationBranchId, [property: JsonRequired] decimal Amount);
    public sealed record ReceiveCashTransferRequest([property: JsonRequired] Guid DestinationCashSessionId);
}
