using MediPOS.Application.Errors;
using MediPOS.Application.Modules.IdentityAccess.Authentication;
using MediPOS.Application.Modules.IdentityAccess.OperationalAccess;
using MediPOS.Application.Modules.Reporting.ExportOwnerReport;
using MediPOS.Application.Modules.Reporting.Operational;
using MediPOS.Application.Tenancy;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.TenancyLicensing;

namespace MediPOS.UnitTests.Modules.Reporting;

public sealed class ReportExportAuthorizationTests
{
    [Theory]
    [InlineData(ReportExportFormat.Xlsx, ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(ReportExportFormat.Pdf, ".pdf", "application/pdf")]
    public async Task OwnerExportUsesServerIdentityAndSafeLimaFilenameAndExactMime(ReportExportFormat format, string extension, string mime)
    {
        var setup = new Setup();
        var result = await setup.Handler.HandleAsync(setup.Query with { Format = format }, TestContext.Current.CancellationToken);
        Assert.Equal($"medipos-sales-2026-10-09{extension}", result.FileName); Assert.Equal(mime, result.ContentType);
        Assert.Equal(setup.Member.UserId, setup.Access.User); Assert.Equal(3, setup.Access.Calls);
        Assert.Equal(setup.Query.TenantId, setup.Reader.Request!.Scope.TenantId);
        Assert.Equal("current_catalog_category — categoría actual del catálogo", result.AppliedFilters.Single(filter => filter.Key == "category_basis").Value);
    }

    [Theory]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task NonOwnersCannotReadOrRender(TenantRole role)
    {
        var setup = new Setup(role);
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query, TestContext.Current.CancellationToken));
        Assert.Equal("reports.owner_required", error.Error.Code); Assert.Null(setup.Reader.Request); Assert.Equal(0, setup.Renderer.Calls);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("branch")]
    [InlineData("license")]
    [InlineData("membership")]
    public async Task InvalidOperationalAccessStopsBeforeTheExportReader(string condition)
    {
        var setup = new Setup(); var query = setup.Query;
        switch (condition)
        {
            case "tenant": query = query with { TenantId = Guid.NewGuid() }; break;
            case "branch": setup.Access.Snapshot = setup.Access.Snapshot with { BranchBelongsToTenant = false }; query = query with { BranchId = Guid.NewGuid() }; break;
            case "license": setup.Access.Snapshot.License!.Suspend(setup.Member.UserId, Setup.Now); break;
            default: setup.Member.Deactivate(Setup.Now); break;
        }
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken));
        Assert.Null(setup.Reader.Request); Assert.Equal(0, setup.Renderer.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task InvalidFormatsTypesOrIrrelevantFiltersFailBeforeReading(int condition)
    {
        var setup = new Setup(); var query = condition switch
        {
            0 => setup.Query with { Format = (ReportExportFormat)999 },
            1 => setup.Query with { ReportType = (OwnerReportType)999 },
            2 => setup.Query with { RotationMode = OwnerProductRotationMode.Top },
            3 => setup.Query with { CriticalOnly = true },
            4 => setup.Query with { Dimension = null },
            5 => setup.Query with { Period = null },
            6 => setup.Query with { Sort = OwnerSalesSort.BranchNameAsc },
            7 => setup.Query with { BusinessProductId = Guid.Empty },
            8 => setup.Query with { ExpirationRange = new(new(2026, 1, 1), new(2026, 1, 2)) },
            _ => setup.Query with { ReportType = OwnerReportType.InventoryCapital, Period = null, Dimension = null, EmployeeMembershipId = Guid.NewGuid() },
        };
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken));
        Assert.Null(setup.Reader.Request); Assert.Equal(0, setup.Access.Calls);
    }

    [Fact]
    public async Task CommissionExportsPreserveTheExistingThirtyOneDayPeriodLimit()
    {
        var setup = new Setup();
        var query = new ExportOwnerReportQuery(setup.Member.TenantId, OwnerReportType.Commissions, ReportExportFormat.Xlsx,
            Period: new(OperationalPeriodType.Custom, new(2026, 1, 1), new(2026, 2, 1)));
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(query, TestContext.Current.CancellationToken));
        Assert.Equal("commissions.report_invalid_period", error.Error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevokedOwnerAfterReadOrRenderNeverReceivesBytes(bool afterRender)
    {
        var setup = new Setup();
        if (afterRender) setup.Renderer.OnRender = () => setup.Member.Deactivate(Setup.Now);
        else setup.Reader.OnRead = () => setup.Member.Deactivate(Setup.Now);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query, TestContext.Current.CancellationToken));
        Assert.Equal(afterRender ? 1 : 0, setup.Renderer.Calls);
    }

    [Fact]
    public async Task AnIncompletePageCannotBeExportedAsACompleteReport()
    {
        var setup = new Setup(); var data = (SalesExportData)setup.Reader.Snapshot.Data;
        setup.Reader.Snapshot = setup.Reader.Snapshot with { Data = data with { Page = data.Page with { Totals = data.Page.Totals with { GroupCount = 2 } } } };
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query, TestContext.Current.CancellationToken));
        Assert.Equal("reports.corrupted_data", error.Error.Code); Assert.Equal(0, setup.Renderer.Calls);
    }

    [Fact]
    public async Task OversizeGeneratedFilesAreRejectedWithoutReturningPartialBytes()
    {
        var setup = new Setup(); setup.Renderer.Bytes = new byte[ReportExportLimits.MaximumBytes + 1];
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Handler.HandleAsync(setup.Query, TestContext.Current.CancellationToken));
        Assert.Equal("reports.export_too_large", error.Error.Code);
    }

    [Fact]
    public async Task CancellationAndServerTimeoutAreDistinctAndReachTheReader()
    {
        var setup = new Setup(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Handler.HandleAsync(setup.Query, cancelled.Token));
        Assert.Null(setup.Reader.Request);
        setup = new Setup(); setup.Reader.Delay = true;
        var handler = new ExportOwnerReportHandler(setup.Resolver, setup.Reader, setup.Renderer, new Clock(), new() { Timeout = TimeSpan.FromMilliseconds(20) });
        var error = await Assert.ThrowsAsync<ApplicationErrorException>(() => handler.HandleAsync(setup.Query, TestContext.Current.CancellationToken));
        Assert.Equal("reports.export_timeout", error.Error.Code); Assert.Equal(0, setup.Renderer.Calls);
    }

    private sealed class Setup
    {
        internal static readonly DateTimeOffset Now = new(2026, 10, 10, 4, 30, 0, TimeSpan.Zero);
        internal Setup(TenantRole role = TenantRole.Owner)
        {
            var document = ReportExportRenderingTests.Document(OwnerReportType.Sales);
            Member = Membership.Create(document.Request.Query.TenantId, Guid.NewGuid(), role, Now.AddDays(-1));
            Access = new(new(true, Member, License.Create(Member.TenantId, Now.AddDays(-1), Now.AddDays(30), 3, LicenseStatus.Active, Member.UserId, Now.AddDays(-1)), true, true, []));
            Resolver = new(new Identity(Member.UserId), Access, new TenantDataContext());
            Query = document.Request.Query; Reader = new(document.Snapshot);
            Handler = new(Resolver, Reader, Renderer, new Clock(), new());
        }
        internal Membership Member { get; }
        internal Access Access { get; }
        internal ResolveAccessContextHandler Resolver { get; }
        internal ExportOwnerReportQuery Query { get; }
        internal Reader Reader { get; }
        internal Renderer Renderer { get; } = new();
        internal ExportOwnerReportHandler Handler { get; }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Setup.Now; }
    private sealed class Identity(Guid user) : IAuthenticatedMediPosUser { public Guid? UserId => user; }
    private sealed class Access(OperationalAccessSnapshot snapshot) : IOperationalAccessReader
    {
        internal OperationalAccessSnapshot Snapshot { get; set; } = snapshot;
        internal Guid User { get; private set; }
        internal int Calls { get; private set; }
        public Task<OperationalAccessSnapshot> ReadAsync(Guid user, Guid tenant, Guid? branch, CancellationToken token)
        { Calls++; User = user; return Task.FromResult(Snapshot); }
    }
    private sealed class Reader(ReportExportSnapshot snapshot) : IReportExportReader
    {
        internal ReportExportSnapshot Snapshot { get; set; } = snapshot;
        internal ReportExportReadRequest? Request { get; private set; }
        internal Action? OnRead { get; set; }
        internal bool Delay { get; set; }
        public async Task<ReportExportSnapshot> ReadAsync(ReportExportReadRequest request, CancellationToken token)
        { Request = request; if (Delay) await Task.Delay(Timeout.InfiniteTimeSpan, token); OnRead?.Invoke(); return Snapshot; }
    }
    private sealed class Renderer : IReportDocumentRenderer
    {
        internal byte[] Bytes { get; set; } = [1]; internal int Calls { get; private set; }
        internal Action? OnRender { get; set; }
        public Task<byte[]> RenderAsync(ReportExportDocument document, ReportExportFormat format, CancellationToken token)
        { Calls++; OnRender?.Invoke(); return Task.FromResult(Bytes); }
    }
}
