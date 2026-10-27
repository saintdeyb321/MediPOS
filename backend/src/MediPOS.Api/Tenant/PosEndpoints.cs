using System.Text.Json.Serialization;
using MediPOS.Application.Modules.Cash;
using MediPOS.Application.Modules.Cash.CloseCashSession;
using MediPOS.Application.Modules.Cash.GetActiveCashSessions;
using MediPOS.Application.Modules.Cash.GetCashSessionReconciliation;
using MediPOS.Application.Modules.Cash.OpenCashSession;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.ConfirmSale;
using MediPOS.Application.Modules.SalesPos.CreateSaleDraft;
using MediPOS.Application.Modules.SalesPos.GetInternalTicket;
using MediPOS.Application.Modules.SalesPos.GetSaleDraft;
using MediPOS.Application.Modules.SalesPos.ReplaceSaleLines;
using MediPOS.Application.Modules.SalesPos.VoidSale;

namespace MediPOS.Api.Tenant;

public static partial class TenantEndpoints
{
    private static void MapPos(RouteGroupBuilder tenants)
    {
        var branches = tenants.MapGroup("/branches/{branchId}");
        branches.MapGet("/sales/{saleId}", async (HttpContext c, GetSaleStateHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "saleId"), c.RequestAborted)))
            .Produces<SaleDraftDetails>().WithName("tenant.sales.read");
        branches.MapPost("/cash-sessions", async (HttpContext c, OpenCashSessionHandler handler) =>
        {
            var input = await TenantHttp.Read<OpenCashRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), input.OpeningAmount), c.RequestAborted), 201);
        }).Accepts<OpenCashRequest>("application/json").Produces<OpenCashSessionDetails>(201).WithName("tenant.cash.open");
        branches.MapGet("/cash-sessions/current", async (HttpContext c, CashSessionStatusQueries queries) => TenantHttp.Response(await queries.CurrentAsync(TenantHttp.Tenant(c), TenantHttp.Branch(c), c.RequestAborted)))
            .Produces<CurrentCashSession>().WithName("tenant.cash.current");
        branches.MapGet("/cash-sessions/{cashSessionId}", async (HttpContext c, CashSessionStatusQueries queries) => TenantHttp.Response(await queries.StatusAsync(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "cashSessionId"), c.RequestAborted)))
            .Produces<CashSessionStatusDetails>().WithName("tenant.cash.status");
        branches.MapGet("/cash-sessions/{cashSessionId}/reconciliation", async (HttpContext c, GetCashSessionReconciliationHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "cashSessionId")), c.RequestAborted)))
            .Produces<CashSessionReconciliationDetails>().WithName("tenant.cash.reconciliation");
        branches.MapPost("/cash-sessions/{cashSessionId}/close", async (HttpContext c, CloseCashSessionHandler handler) =>
        {
            var input = await TenantHttp.Read<CloseCashRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "cashSessionId"), input.CountedCashAmount), c.RequestAborted));
        }).Accepts<CloseCashRequest>("application/json").Produces<CashSessionReconciliationDetails>().WithName("tenant.cash.close");
        tenants.MapGet("/cash-sessions/active", async (HttpContext c, GetActiveCashSessionsHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.OptionalId(c, "branchId"), TenantHttp.Offset(c), TenantHttp.Limit(c)), c.RequestAborted)))
            .Produces<IReadOnlyList<ActiveCashSessionDetails>>().WithName("tenant.cash.active").WithQuery<TenantOpenApi.BranchPage>();
        branches.MapPost("/sales", async (HttpContext c, CreateSaleDraftHandler handler) =>
        {
            await TenantHttp.Read<EmptyRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c)), c.RequestAborted), 201);
        }).Accepts<EmptyRequest>("application/json").Produces<SaleDraftDetails>(201).WithName("tenant.sales.create_draft").AddEndpointFilter(new RecoverableWriteFilter("sale-draft"));
        branches.MapGet("/sales/{saleId}/draft", async (HttpContext c, GetSaleDraftHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "saleId")), c.RequestAborted)))
            .Produces<SaleDraftDetails>().WithName("tenant.sales.read_draft");
        branches.MapPut("/sales/{saleId}/lines", async (HttpContext c, ReplaceSaleLinesHandler handler) =>
        {
            var input = await TenantHttp.Read<SaleLinesRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "saleId"), input.ExpectedVersion, input.Lines), c.RequestAborted));
        }).Accepts<SaleLinesRequest>("application/json").Produces<SaleDraftDetails>().WithName("tenant.sales.lines");
        branches.MapPost("/sales/{saleId}/confirm", async (HttpContext c, ConfirmSaleHandler handler) =>
        {
            var input = await TenantHttp.Read<ConfirmSaleRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "saleId"), input.ExpectedVersion, input.Payments), c.RequestAborted));
        }).Accepts<ConfirmSaleRequest>("application/json").Produces<ConfirmSaleResult>().WithName("tenant.sales.confirm");
        branches.MapGet("/sales/{saleId}/ticket", async (HttpContext c, GetInternalTicketHandler handler) =>
            TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "saleId")), c.RequestAborted)))
            .Produces<InternalTicket>().WithName("tenant.sales.ticket");
        branches.MapPost("/sales/{saleId}/void", async (HttpContext c, VoidSaleHandler handler) =>
        {
            var input = await TenantHttp.Read<VoidSaleRequest>(c);
            return TenantHttp.Response(await handler.HandleAsync(new(TenantHttp.Tenant(c), TenantHttp.Branch(c), TenantHttp.Id(c, "saleId"), input.ExpectedVersion, input.Reason), c.RequestAborted));
        }).Accepts<VoidSaleRequest>("application/json").Produces<VoidSaleResult>().WithName("tenant.sales.void");
    }
    public sealed record OpenCashRequest([property: JsonRequired] decimal OpeningAmount);
    public sealed record CloseCashRequest([property: JsonRequired] decimal CountedCashAmount);
    public sealed record SaleLinesRequest([property: JsonRequired] uint ExpectedVersion, [property: JsonRequired] IReadOnlyList<SaleLineInput> Lines);
    public sealed record ConfirmSaleRequest([property: JsonRequired] uint ExpectedVersion, [property: JsonRequired] IReadOnlyList<SalePaymentInput> Payments);
    public sealed record VoidSaleRequest([property: JsonRequired] uint ExpectedVersion, [property: JsonRequired] string Reason);
}
