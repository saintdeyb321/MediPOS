using System.Text.Json;
using MediPOS.Application.Errors;
using MediPOS.Application.Modules.OfflineSync;
using MediPOS.Domain.Modules.IdentityAccess;
using MediPOS.Domain.Modules.OfflineSync;

namespace MediPOS.UnitTests.Modules.OfflineSync;

public sealed class BranchCatalogTests
{
    [Theory]
    [InlineData(TenantRole.Owner)]
    [InlineData(TenantRole.Cashier)]
    [InlineData(TenantRole.Pharmacist)]
    public async Task CurrentAuthorizedDeviceDownloadsCompleteBoundedAdvisoryProjection(TenantRole role)
    {
        var setup = new BranchCatalogTestSetup(role: role); var page = await setup.ReadSnapshot();
        Assert.Equal(1, page.SchemaVersion); Assert.Equal(setup.Sync.Device.BranchId, page.BranchId); Assert.True(page.IsFullSnapshot);
        Assert.False(page.HasMore); Assert.False(page.RequiresFullResync); Assert.True(page.StockIsAdvisory); Assert.Equal(3, page.Items.Count);
        Assert.Equal(101, setup.Reader.Takes); Assert.Equal(0, setup.Reader.Active);
        Assert.All(page.Items, item => { Assert.Equal(0m, item.Stock!.PhysicalStockBase); Assert.Equal(2, item.Product!.Units.Count); Assert.False(item.Product.Units[1].IsActive); });
        var packet = setup.Reader.Decode(page.NextCursor!); Assert.Equal("checkpoint", packet.Kind); Assert.Equal(setup.Sync.Device.Id, packet.DeviceId);
        var serialized = JsonSerializer.Serialize(page);
        foreach (var forbidden in new[] { "CursorKey", "StockBuckets", "InventoryLotId", "Supplier", "PurchaseCost", "Commission", "Audit", "Payment", "MembershipId", "Payload" })
            Assert.DoesNotContain(forbidden, serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SnapshotKeysetPagesAreStableAndFinishAtASafeDeltaCheckpoint()
    {
        var setup = new BranchCatalogTestSetup(5); var first = await setup.ReadSnapshot(size: 2); Assert.True(first.HasMore);
        var second = await setup.ReadSnapshot(first.NextCursor, 2); var retry = await setup.ReadSnapshot(first.NextCursor, 2);
        Assert.Equal(second.NextCursor, retry.NextCursor); Assert.Equal(second.Items.Select(item => item.BusinessProductId), retry.Items.Select(item => item.BusinessProductId));
        var last = await setup.ReadSnapshot(second.NextCursor, 2); Assert.False(last.HasMore);
        Assert.Equal(5, first.Items.Concat(second.Items).Concat(last.Items).Select(item => item.BusinessProductId).Distinct().Count());
        Assert.Empty((await setup.ReadChanges(last.NextCursor, 2)).Items);
        setup.Reader.Append(setup.Reader.Products[0]);
        Assert.Single((await setup.ReadChanges(last.NextCursor, 2)).Items);
    }

    [Fact]
    public async Task SnapshotContinuationExplicitlyRestartsAfterConcurrentMutation()
    {
        var setup = new BranchCatalogTestSetup(3); var first = await setup.ReadSnapshot(size: 1); setup.Reader.Append(setup.Reader.Products[0]);
        var next = await setup.ReadSnapshot(first.NextCursor, 1); Assert.True(next.RequiresFullResync); Assert.Equal(BranchCatalogPolicy.ResyncCode, next.ResultCode);
        Assert.Empty(next.Items); Assert.Null(next.NextCursor);
        Assert.False((await setup.ReadSnapshot(size: 1)).RequiresFullResync);
    }

    [Fact]
    public async Task DeltaPagesKeepAllVersionsIncludingTombstoneAndReactivationAcrossNewCommits()
    {
        var setup = new BranchCatalogTestSetup(1); var baseline = await setup.ReadSnapshot(size: 1); var product = setup.Reader.Products[0];
        setup.Reader.Append(product with { Projection = product.Projection! with { Product = product.Projection.Product with { RetailPrice = 3m } } });
        setup.Reader.Append(product with { Operation = "tombstone", Projection = null }); setup.Reader.Append(product);
        var first = await setup.ReadChanges(baseline.NextCursor, 1); Assert.True(first.HasMore); Assert.Equal(3m, Assert.Single(first.Items).Product!.RetailPrice);
        setup.Reader.Append(product with { Projection = product.Projection! with { StockBuckets = [new(null, 7m)] } });
        var second = await setup.ReadChanges(first.NextCursor, 1); Assert.Equal("tombstone", Assert.Single(second.Items).Operation); Assert.Null(second.Items[0].Product);
        var repeat = await setup.ReadChanges(first.NextCursor, 1); Assert.Equal(second.NextCursor, repeat.NextCursor); Assert.Equal(second.Items, repeat.Items);
        var last = await setup.ReadChanges(second.NextCursor, 1); Assert.Equal(3, Assert.Single(last.Items).Version); Assert.False(last.HasMore);
        var following = await setup.ReadChanges(last.NextCursor, 1); Assert.Equal(4, Assert.Single(following.Items).Version); Assert.Equal(7m, following.Items[0].Stock!.SellableStockBase);
    }

    [Fact]
    public async Task InactiveProductsRemainInSnapshotAndUnitsAreCompleteReplacementCollections()
    {
        var setup = new BranchCatalogTestSetup(1); var original = setup.Reader.Products[0];
        setup.Reader.Products[0] = original with { Projection = original.Projection! with { Product = original.Projection.Product with { IsActive = false } } };
        Assert.False(Assert.Single((await setup.ReadSnapshot()).Items).Product!.IsActive);
        var baseline = await setup.ReadSnapshot(); var replacement = original with { Projection = original.Projection! with { Product = original.Projection.Product with { Units = [original.Projection.Product.Units[0]] } } };
        setup.Reader.Append(replacement); Assert.Single(Assert.Single((await setup.ReadChanges(baseline.NextCursor)).Items).Product!.Units);
    }

    [Theory]
    [InlineData("medicine", "7", "5", "2")]
    [InlineData("retail", "7", "7", "0")]
    public void StockSeparatesPhysicalSellableAndExpiredWithoutImposingPharmaceuticalExpiryOnRetail(string type, string physical, string sellable, string expired)
    {
        var setup = new BranchCatalogTestSetup(1); var row = setup.Reader.Products[0]; var day = new DateOnly(2026, 10, 6);
        row = row with { Projection = row.Projection! with { Product = row.Projection.Product with { ProductType = type }, StockBuckets = [new(day.AddDays(-1), 2m), new(day, 5m)] } };
        var result = BranchCatalogPolicy.Project(row, day, setup.Sync.Clock.Now);
        Assert.Equal(decimal.Parse(physical, System.Globalization.CultureInfo.InvariantCulture), result.Stock!.PhysicalStockBase);
        Assert.Equal(decimal.Parse(sellable, System.Globalization.CultureInfo.InvariantCulture), result.Stock.SellableStockBase);
        Assert.Equal(decimal.Parse(expired, System.Globalization.CultureInfo.InvariantCulture), result.Stock.ExpiredStockBase);
    }

    [Fact]
    public async Task LimaMidnightRequiresResyncEvenWithoutAnyWriteAndNewSnapshotRecalculatesExpiry()
    {
        var setup = new BranchCatalogTestSetup(1); var row = setup.Reader.Products[0];
        setup.Reader.Products[0] = row with { Projection = row.Projection! with { Product = row.Projection.Product with { ProductType = "medicine" }, StockBuckets = [new(new(2026, 10, 6), 1m)] } };
        setup.Sync.Clock.Now = new(2026, 10, 7, 4, 59, 59, TimeSpan.Zero);
        var old = await setup.ReadSnapshot(); Assert.Equal(1m, old.Items[0].Stock!.SellableStockBase);
        setup.Sync.Clock.Now = setup.Sync.Clock.Now.AddSeconds(1);
        Assert.True((await setup.ReadChanges(old.NextCursor)).RequiresFullResync);
        var fresh = await setup.ReadSnapshot(); Assert.Equal(0m, fresh.Items[0].Stock!.SellableStockBase); Assert.Equal(1m, fresh.Items[0].Stock!.ExpiredStockBase);
        Assert.Equal(0, setup.Reader.Version);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("tenant")]
    [InlineData("branch")]
    [InlineData("device")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("position")]
    [InlineData("size")]
    [InlineData("signature")]
    [InlineData("garbage")]
    public async Task InvalidCursorsNeverDiscloseTheirForeignMetadataOrSilentlyReturnAnEmptySuccess(string scenario)
    {
        var setup = new BranchCatalogTestSetup(); var baseline = await setup.ReadSnapshot(); var packet = setup.Reader.Decode(baseline.NextCursor!);
        packet = scenario switch
        {
            "schema" => packet with { SchemaVersion = 2 },
            "tenant" => packet with { TenantId = Guid.NewGuid() },
            "branch" => packet with { BranchId = Guid.NewGuid() },
            "device" => packet with { DeviceId = Guid.NewGuid() },
            "expired" => packet with { ExpiresAtUtc = setup.Sync.Clock.Now.AddSeconds(-1) },
            "future" => packet with { ThroughVersion = 100 },
            "position" => packet with { AfterProductId = Guid.NewGuid() },
            "size" => packet with { PageSize = 500 },
            _ => packet,
        };
        var cursor = setup.Reader.Encode(packet);
        if (scenario == "signature") cursor = cursor[..^1] + (cursor[^1] == 'A' ? 'B' : 'A');
        if (scenario == "garbage") cursor = "not-a-cursor";
        var result = await setup.ReadChanges(cursor); Assert.True(result.RequiresFullResync); Assert.Empty(result.Items); Assert.Equal(setup.Sync.Device.BranchId, result.BranchId);
        Assert.Equal(BranchCatalogPolicy.ResyncCode, result.ResultCode); Assert.Null(result.NextCursor);
    }

    [Theory]
    [InlineData("session")]
    [InlineData("member")]
    [InlineData("license")]
    [InlineData("branch")]
    [InlineData("tenant")]
    [InlineData("revoked")]
    [InlineData("device_owner")]
    public async Task CurrentAccessAndExactDeviceOwnershipAreRequiredBeforeAnyProjectionRead(string scenario)
    {
        var setup = new BranchCatalogTestSetup(); Guid? branch = null; var tenant = setup.Sync.Member.TenantId;
        switch (scenario)
        {
            case "session": setup.Sync.Identity.UserId = null; break;
            case "member": setup.Sync.Member.Deactivate(setup.Sync.Clock.Now); break;
            case "license": setup.Sync.Clock.Now = setup.Sync.License.ExpiresAt; break;
            case "branch": branch = Guid.NewGuid(); break;
            case "tenant": tenant = Guid.NewGuid(); break;
            case "revoked": setup.Sync.Device.Revoke(setup.Sync.Member.Id, setup.Sync.Clock.Now); break;
            case "device_owner": var other = SyncDevice.Register(tenant, Guid.NewGuid(), setup.Sync.Device.BranchId, "otro", setup.Sync.Clock.Now); setup.Sync.Data.Devices[setup.Sync.Device.Id] = other; break;
        }
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.Snapshot.HandleAsync(new(tenant, setup.Sync.Device.Id, BranchId: branch), TestContext.Current.CancellationToken)); Assert.Equal(0, setup.Reader.Opens);
    }

    [Fact]
    public async Task DeviceRevocationDuringReadIsRecheckedOutsideItsMvccSession()
    {
        var setup = new BranchCatalogTestSetup(); setup.Reader.DuringRead = () => setup.Sync.Device.Revoke(setup.Sync.Member.Id, setup.Sync.Clock.Now);
        Assert.Equal("sync.device_revoked", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.ReadSnapshot())).Error.Code); Assert.Equal(0, setup.Reader.Active);
    }

    [Theory]
    [InlineData(TenantRole.Cashier, "assignment")]
    [InlineData(TenantRole.Cashier, "foreign_branch")]
    [InlineData(TenantRole.Cashier, "schedule")]
    [InlineData(TenantRole.Pharmacist, "assignment")]
    [InlineData(TenantRole.Pharmacist, "foreign_branch")]
    [InlineData(TenantRole.Pharmacist, "schedule")]
    public async Task OperationalRolesCannotDownloadOutsideTheirBranchOrSchedule(TenantRole role, string restriction)
    {
        var setup = new BranchCatalogTestSetup(role: role);
        if (restriction == "assignment") setup.Sync.Access.Assigned = false;
        else if (restriction == "foreign_branch") setup.Sync.Access.BranchBelongs = false;
        else setup.Sync.Clock.Now = setup.Sync.Clock.Now.AddHours(10);
        await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.ReadSnapshot()); Assert.Equal(0, setup.Reader.Opens);
    }

    [Fact]
    public async Task EmptyCatalogStillProvidesTheCheckpointForFutureProductCreation()
    {
        var setup = new BranchCatalogTestSetup(0); var page = await setup.ReadSnapshot();
        Assert.Empty(page.Items); Assert.False(page.HasMore); Assert.NotNull(page.NextCursor);
        var other = new BranchCatalogTestSetup(1); setup.Reader.Append(other.Reader.Products[0]);
        Assert.Single((await setup.ReadChanges(page.NextCursor)).Items);
    }

    [Fact]
    public async Task AReadCrossingLimaMidnightDiscardsItsOldSellableProjection()
    {
        var setup = new BranchCatalogTestSetup(1); setup.Sync.Clock.Now = new(2026, 10, 7, 4, 59, 59, TimeSpan.Zero);
        setup.Reader.DuringRead = () => setup.Sync.Clock.Now = setup.Sync.Clock.Now.AddSeconds(1);
        var result = await setup.ReadSnapshot(); Assert.True(result.RequiresFullResync); Assert.Empty(result.Items);
    }

    [Fact]
    public void PharmaceuticalStrengthUnitsAndFractionalStockArePreservedExactly()
    {
        var setup = new BranchCatalogTestSetup(1); var original = setup.Reader.Products[0];
        var medicine = new BranchCatalogMedicine(["Ingrediente"], "5 mg/5 mL", "Solución", "Oral", "REG-123", "solution", "oral", "key",
            [new("ingrediente", "1 mg/mL")]);
        var row = original with
        {
            Projection = original.Projection! with
            {
                Product = original.Projection.Product with { ProductType = "medicine", Medicine = medicine },
                StockBuckets = [new(new(2026, 10, 6), 99999999.123456789012m), new(new(2026, 10, 6), 0.000000000001m)]
            }
        };
        var item = BranchCatalogPolicy.Project(row, new(2026, 10, 6), setup.Sync.Clock.Now);
        Assert.Equal("5 mg/5 mL", item.Product!.Medicine!.NormalizedStrength);
        Assert.Equal("1 mg/mL", Assert.Single(item.Product.Medicine.Components).StrengthNormalized);
        Assert.Equal(99999999.123456789013m, item.Stock!.SellableStockBase);
    }

    [Fact]
    public async Task MissingRetainedVersionAndGapBothRequireExplicitResync()
    {
        var setup = new BranchCatalogTestSetup(1); var baseline = await setup.ReadSnapshot(); setup.Reader.Version = 1;
        Assert.True((await setup.ReadChanges(baseline.NextCursor)).RequiresFullResync);
        setup.Reader.MinimumRetained = 1; Assert.True((await setup.ReadChanges(baseline.NextCursor)).RequiresFullResync);
        Assert.True((await setup.ReadChanges(null)).RequiresFullResync);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public async Task PageLimitsAreValidatedBeforeReading(int size)
    { var setup = new BranchCatalogTestSetup(); Assert.Equal("sync.catalog_page_size_invalid", (await Assert.ThrowsAsync<ApplicationErrorException>(() => setup.ReadSnapshot(size: size))).Error.Code); Assert.Equal(0, setup.Reader.Opens); }
    [Fact]
    public async Task MaximumPageAndCancellationRemainBounded()
    {
        var setup = new BranchCatalogTestSetup(501); Assert.Equal(500, (await setup.ReadSnapshot(size: 500)).Items.Count); Assert.Equal(501, setup.Reader.Takes);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Snapshot.HandleAsync(new(setup.Sync.Member.TenantId, setup.Sync.Device.Id), cancelled.Token));
    }
}
