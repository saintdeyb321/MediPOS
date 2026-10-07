using System.Data.Common;
using System.Text.Json;
using MediPOS.Application.Modules.SalesPos;
using MediPOS.Application.Modules.SalesPos.GetInternalTicket;
using MediPOS.Infrastructure;
using MediPOS.Infrastructure.Persistence;
using MediPOS.IntegrationTests.Modules.Cash;
using MediPOS.IntegrationTests.Modules.IdentityAccess;
using MediPOS.IntegrationTests.Modules.TenancyLicensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MediPOS.IntegrationTests.Modules.SalesPos;

internal static class InternalTicketTestData
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static GetInternalTicketQuery Query(SaleCheckoutTestData.Rows rows) =>
        new(rows.Tenant.TenantId, rows.Tenant.Identity.BranchId, rows.Draft.SaleId);

    internal static Task<InternalTicket> ReadAsync(IServiceProvider source, SaleCheckoutTestData.Rows rows)
    {
        CashSessionTestData.Authenticate(source, rows.Tenant.Identity.UserId);
        return source.GetRequiredService<GetInternalTicketHandler>().HandleAsync(Query(rows), TestContext.Current.CancellationToken);
    }

    internal static async Task<string> HistoryAsync(PostgreSqlFixture fixture, Guid tenantId)
    {
        await using var context = fixture.CreateContext(tenantId);
        var sales = await context.Sales.AsNoTracking().Include(s => s.Lines.OrderBy(l => l.Id)).AsSingleQuery().OrderBy(s => s.Id)
            .Select(s => new { Sale = s, Version = EF.Property<uint>(s, "Version") }).ToArrayAsync(TestContext.Current.CancellationToken);
        var payments = await context.SalePayments.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        var reversals = await context.SalePaymentReversals.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        var stock = await context.StockMovements.AsNoTracking().OrderBy(m => m.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        var lots = await context.InventoryLots.AsNoTracking().OrderBy(l => l.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        var audits = await context.AuditLogs.AsNoTracking().OrderBy(a => a.Id).ToArrayAsync(TestContext.Current.CancellationToken);
        return JsonSerializer.Serialize(new { sales, payments, reversals, stock, lots, audits }, JsonOptions);
    }

    internal static ServiceProvider CreateServices(PostgreSqlFixture fixture, QueryObserver? observer = null, bool singleConnection = false)
    {
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { ApplicationName = Guid.NewGuid().ToString("N"), NoResetOnClose = true };
        if (singleConnection) connection.MaxPoolSize = 1;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:MediPosDatabase"] = connection.ConnectionString }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new IdentityAccessTestSetup.Clock());
        services.AddInfrastructure(configuration);
        if (observer is not null) services.AddDbContext<MediPosDbContext>(options => options.AddInterceptors(observer));
        services.AddIdentityAuthentication<IdentityAccessTestSetup.TestGoogleIdentitySource, IdentityAccessTestSetup.TestServerSession>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    internal sealed class QueryObserver : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
