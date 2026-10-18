using System.Text.RegularExpressions;
using MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialRefresh;

public sealed class OfficialRefreshGuardMigrationTests
{
    [Fact]
    public void ProjectionCorrectionOnlyGroupsExtractedProposalsAndRetainsEveryAuthorizationAndHistoryGuard()
    {
        var original = Assert.Single(new FixOfficialCatalogRefreshSourceOrder().UpOperations.OfType<SqlOperation>()).Sql;
        var function = Regex.Match(original, @"CREATE OR REPLACE FUNCTION official_refresh_guard\(\)[\s\S]*?END \$guard\$;",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        Assert.True(function.Success);
        Assert.Contains("OLD.previous_snapshot->'Proposal' - ARRAY[", function.Value, StringComparison.Ordinal);
        Assert.Contains("OLD.proposed_snapshot->'Proposal' - ARRAY[", function.Value, StringComparison.Ordinal);
        var expected = function.Value
            .Replace("OLD.previous_snapshot->'Proposal' - ARRAY[", "(OLD.previous_snapshot->'Proposal') - ARRAY[", StringComparison.Ordinal)
            .Replace("OLD.proposed_snapshot->'Proposal' - ARRAY[", "(OLD.proposed_snapshot->'Proposal') - ARRAY[", StringComparison.Ordinal);
        var correction = new FixOfficialRefreshJsonProjectionPrecedence();
        var sql = Assert.IsType<SqlOperation>(Assert.Single(correction.UpOperations)).Sql;
        Assert.Equal(expected.ReplaceLineEndings("\n"), sql.Trim().ReplaceLineEndings("\n"));
        Assert.Throws<NotSupportedException>(() => { _ = correction.DownOperations; });
    }

    [Fact]
    public void CorrectiveMigrationOnlyQualifiesApprovalHistoryWithoutWeakeningTheExistingGuard()
    {
        var original = Assert.Single(new FixOfficialCatalogRefreshSourceOrder().UpOperations.OfType<SqlOperation>()).Sql;
        var function = Regex.Match(original, @"CREATE OR REPLACE FUNCTION official_refresh_global_guard\(\)[\s\S]*?END \$guard\$;",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        Assert.True(function.Success);
        const string ambiguous = "FROM official_catalog_version_history WHERE refresh_id = r.id AND version = r.approved_version AND action = 'approve'";
        const string qualified = "FROM official_catalog_version_history h WHERE h.refresh_id = r.id AND h.version = r.approved_version AND h.action = 'approve'";
        Assert.Contains(ambiguous, function.Value, StringComparison.Ordinal);
        var correction = new FixOfficialRefreshApprovalVersionReference();
        var sql = Assert.IsType<SqlOperation>(Assert.Single(correction.UpOperations)).Sql;
        Assert.Equal(function.Value.Replace(ambiguous, qualified, StringComparison.Ordinal).ReplaceLineEndings("\n"), sql.Trim().ReplaceLineEndings("\n"));
        Assert.Throws<NotSupportedException>(() => { _ = correction.DownOperations; });
    }
}
