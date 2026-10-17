using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MediPOS.Infrastructure.Persistence;
using MediPOS.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MediPOS.UnitTests.Recovery;

public sealed class RuntimeRecoveryEvidenceTests
{
    [Fact]
    public void InvitationsDenyActorlessReadsWithoutClaimingOwnerOrTicketIsolation()
    {
        Assert.Equal(0, RuntimeRecoveryProbe.ExpectedActorlessRows("employee_invitations", 7));
        Assert.Equal(7, RuntimeRecoveryProbe.ExpectedActorlessRows("audit_logs", 7));
        var table = new RuntimeTableEvidence("employee_invitations", [new(1, 0, 0, 0), new(2, 0, 0, 0)]);
        var evidence = RuntimeRecoveryEvidence.FromChecks(2, [Table("audit_logs", 2), table]);
        Assert.Equal("ACTORLESS_DENIAL_ONLY", table.ReadScope);
        Assert.Equal("ACTOR_CONTEXT_NOT_EXERCISED", table.CrossTenantCoverage);
        Assert.Equal("SELECTED_PAIR_READ_CHECKED_WITH_GAPS", evidence.TenantIsolation);
        Assert.Contains("INVITATION_OWNER_AND_TICKET_RLS", evidence.PendingVerifications);
        Assert.Contains("PRIVATE_TABLE_ROW_COVERAGE", evidence.PendingVerifications);
    }

    [Fact]
    public void InvitationVisibilityCannotBeBroadenedOrOmittedToManufactureRecoveryApproval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RuntimeRecoveryProbe.ExpectedActorlessRows("employee_invitations", -1));
        Assert.Throws<InvalidOperationException>(() => RuntimeRecoveryEvidence.FromChecks(1, [new("employee_invitations", [new(1, 0, 1, 0)])]));
        Assert.Throws<InvalidOperationException>(() => RuntimeRecoveryEvidence.FromChecks(1, [new("employee_invitations", [new(1, 1, 1, 0)])]));
        Assert.Throws<InvalidOperationException>(() => RuntimeRecoveryEvidence.FromChecks(2, [new("employee_invitations", [new(1, 0, 0, 0)])]));
    }

    [Theory]
    [InlineData(0, "NOT_DEMONSTRATED_NO_TENANTS", "EMPTY_TENANT_CONTEXT_READ")]
    [InlineData(1, "NOT_DEMONSTRATED_SINGLE_TENANT", "SINGLE_TENANT_READ")]
    [InlineData(2, "SELECTED_PAIR_READ_CHECKED", "SELECTED_PAIR_READ")]
    public void EvidenceSeparatesTenantAvailabilityFromChecks(int tenants, string isolation, string scope)
    {
        var evidence = RuntimeRecoveryEvidence.FromChecks(tenants, [Table("audit_logs", tenants)]);
        Assert.Equal(tenants, evidence.TenantsAvailable);
        Assert.Equal(tenants, evidence.TenantsExamined);
        Assert.Equal(isolation, evidence.TenantIsolation);
        Assert.Equal(scope, evidence.IsolationScope);
        Assert.Contains("EXTERNAL_SECURITY_AND_TRANSACTION_REGRESSIONS", evidence.PendingVerifications);
        if (tenants < 2) Assert.Contains("REPRESENTATIVE_TWO_TENANT_REHEARSAL", evidence.PendingVerifications);
        else Assert.DoesNotContain("REPRESENTATIVE_TWO_TENANT_REHEARSAL", evidence.PendingVerifications);
        if (tenants == 0)
        {
            var check = Assert.Single(Assert.Single(evidence.Tables).Checks);
            Assert.Null(check.TenantOrdinal);
            Assert.Equal(0, check.ExpectedRows);
            Assert.Equal(0, check.VisibleRows);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(0, 5)]
    public void TwoTenantEvidenceReportsTablesWithoutRowsForBothContexts(long first, long second)
    {
        var evidence = RuntimeRecoveryEvidence.FromChecks(2,
            [Table("audit_logs", 2), new("sale_payments", [new(1, first, first, 0), new(2, second, second, 0)])]);
        Assert.Equal("SELECTED_PAIR_READ_CHECKED_WITH_GAPS", evidence.TenantIsolation);
        Assert.Equal("SELECTED_PAIR_ROWS_PRESENT", evidence.Tables[0].CrossTenantCoverage);
        Assert.Equal("SELECTED_PAIR_ROWS_INCOMPLETE", evidence.Tables[1].CrossTenantCoverage);
        Assert.Contains("PRIVATE_TABLE_ROW_COVERAGE", evidence.PendingVerifications);
    }

    [Fact]
    public void OnlyTheSelectedPairIsClaimedWhenMoreTenantsExist()
    {
        var evidence = RuntimeRecoveryEvidence.FromChecks(5, [Table("audit_logs", 2)]);
        Assert.Equal(5, evidence.TenantsAvailable);
        Assert.Equal(2, evidence.TenantsExamined);
        Assert.Equal("SELECTED_PAIR_READ", evidence.IsolationScope);
        Assert.Contains("ADDITIONAL_TENANTS", evidence.PendingVerifications);
    }

    [Theory]
    [InlineData(10, 9, 0)]
    [InlineData(10, 11, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(-1, -1, 0)]
    public void IncorrectVisibilityIsRejectedRatherThanMarkedPending(long expected, long visible, long foreign)
    {
        var check = new RuntimeVisibility(1, expected, visible, foreign);
        Assert.Throws<InvalidOperationException>(() => RuntimeRecoveryProbe.RequireVisibility(check));
        Assert.Throws<InvalidOperationException>(() => RuntimeRecoveryEvidence.FromChecks(1, [new("audit_logs", [check])]));
    }

    [Fact]
    public void MissingChecksOrFabricatedEmptyContextCannotBecomeEvidence()
    {
        Assert.Throws<InvalidOperationException>(() => RuntimeRecoveryEvidence.FromChecks(0, []));
        Assert.Throws<InvalidOperationException>(() => RuntimeRecoveryEvidence.FromChecks(2, [Table("audit_logs", 1)]));
        Assert.Throws<InvalidOperationException>(() => RuntimeRecoveryEvidence.FromChecks(0, [Table("audit_logs", 1)]));
        Assert.Throws<InvalidOperationException>(() => RuntimeRecoveryEvidence.FromChecks(0, [new("audit_logs", [new(null, 1, 1, 0)])]));
        Assert.Throws<InvalidOperationException>(() => RuntimeRecoveryEvidence.FromChecks(1, [Table("audit_logs", 1), Table("audit_logs", 1)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyAndSelectedContextsAlwaysInstallTheCommercialRoleGuardWithoutOpeningDatabase(bool selected)
    {
        Guid? tenant = selected ? Guid.Parse("00000000-0000-0000-0000-000000000001") : null;
        using var context = RuntimeRecoveryProbe.CreateRuntimeContext("Host=unused.invalid;Database=synthetic;Username=runtime", tenant);
        var options = context.GetService<IDbContextOptions>().Extensions.OfType<CoreOptionsExtension>().Single();
        Assert.Single(options.Interceptors!.OfType<RuntimeDatabaseRoleInterceptor>());
        Assert.Equal(tenant, context.SelectedTenantId);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task CommercialGuardRejectsUnrestrictedOrMissingRoleCheck(object? result)
    {
        // A unit test of the interceptor decision only; PostgreSQL must verify actual role capabilities externally.
        await using var connection = new ScalarConnection(result);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RuntimeDatabaseRoleInterceptor()
            .ConnectionOpenedAsync(connection, null!, TestContext.Current.CancellationToken));
        Assert.Contains("rolbypassrls", connection.ExecutedSql);
        Assert.Contains("pg_has_role", connection.ExecutedSql);
        Assert.Contains("has_schema_privilege", connection.ExecutedSql);
    }

    [Theory]
    [InlineData(0, false, "NOT_DEMONSTRATED_NO_TENANTS")]
    [InlineData(1, false, "NOT_DEMONSTRATED_SINGLE_TENANT")]
    [InlineData(2, false, "SELECTED_PAIR_READ_CHECKED")]
    [InlineData(0, true, "NOT_DEMONSTRATED_NO_TENANTS")]
    [InlineData(1, true, "NOT_DEMONSTRATED_SINGLE_TENANT")]
    [InlineData(2, true, "SELECTED_PAIR_READ_CHECKED")]
    public void WorkflowSerializationNeverTurnsVerifiedIntegrityIntoProductionApproval(int tenants, bool rehearse, string isolation)
    {
        var snapshot = new RecoverySnapshot(["known"], new() { ["audit_logs"] = new(0, "digest") }, "schema");
        var manifest = new BackupManifest("backup", DateTimeOffset.UtcNow, "RecoveryLab", "operator", "protected", "cluster", 18,
            "postgresql18-custom-full", 1, "sha256", snapshot);
        var evidence = RuntimeRecoveryEvidence.FromChecks(tenants, [Table("audit_logs", tenants)]);
        var report = RecoveryWorkflow.CreateResult(rehearse, manifest, "restore", snapshot.Migrations, snapshot.Migrations, evidence, new());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report, RecoveryFiles.JsonOptions));
        var root = json.RootElement;
        Assert.Equal(rehearse ? "RESTORE_AND_MIGRATION_VERIFIED" : "RESTORE_VERIFIED", root.GetProperty("State").GetString());
        Assert.Equal("VERIFIED", root.GetProperty("RestoreIntegrity").GetString());
        Assert.Equal(1, root.GetProperty("ProcedureVersion").GetInt32());
        Assert.Equal(2, root.GetProperty("EvidenceVersion").GetInt32());
        Assert.Equal(tenants, root.GetProperty("TenantsExamined").GetInt32());
        Assert.Equal(isolation, root.GetProperty("TenantIsolation").GetString());
        Assert.Equal("CHECKED", root.GetProperty("RuntimeRole").GetString());
        Assert.Equal("NOT_AUTHORIZED", root.GetProperty("ProductionRecovery").GetString());
        Assert.Equal("PENDING_EXTERNAL_EXECUTION", root.GetProperty("RegressionSuite").GetString());
        Assert.NotEmpty(root.GetProperty("PendingVerifications").EnumerateArray());
        Assert.Equal(tenants == 0 ? "NO_TENANTS" : tenants == 1 ? "SINGLE_TENANT_ONLY" : "SELECTED_PAIR_ROWS_PRESENT",
            root.GetProperty("PrivateTableChecks")[0].GetProperty("CrossTenantCoverage").GetString());
        Assert.DoesNotContain("TWO_TENANTS_CHECKED", root.GetRawText());
        Assert.Equal(1, manifest.ProcedureVersion);
        Assert.Equal("NOT_EXECUTED", manifest.RestoreRehearsal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ActualIntegrityMismatchStillRejectsEveryTenantPopulation(int tenants)
    {
        var original = new RecoverySnapshot(["known"], new() { ["audit_logs"] = new(10, "original") }, "schema");
        var actual = original with { Tables = new() { ["audit_logs"] = new(9, "missing-history") } };
        Assert.Throws<InvalidOperationException>(() => RecoveryInspection.Compare(original, actual));
        // Tenant classification does not replace the comparison which RestoreAsync runs before the probe.
        Assert.Equal(tenants, RuntimeRecoveryEvidence.FromChecks(tenants, [Table("audit_logs", tenants)]).TenantsExamined);
    }

    private static RuntimeTableEvidence Table(string name, int tenants) => new(name, tenants == 0 ? [new(null, 0, 0, 0)] :
        Enumerable.Range(1, tenants).Select(ordinal => new RuntimeVisibility(ordinal, 5, 5, 0)).ToArray());

    private sealed class ScalarConnection(object? result) : DbConnection
    {
        public string ExecutedSql { get; private set; } = "";
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "synthetic";
        public override string DataSource => "unit-test";
        public override string ServerVersion => "unit-test";
        public override ConnectionState State => ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Open() => throw new NotSupportedException();
        public override void Close() => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new ScalarCommand(this, result);

        private sealed class ScalarCommand(ScalarConnection connection, object? scalar) : DbCommand
        {
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = connection;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
            public override object? ExecuteScalar() { connection.ExecutedSql = CommandText; return scalar; }
            public override int ExecuteNonQuery() => throw new NotSupportedException();
            public override void Cancel() => throw new NotSupportedException();
            public override void Prepare() => throw new NotSupportedException();
            protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        }
    }
}
