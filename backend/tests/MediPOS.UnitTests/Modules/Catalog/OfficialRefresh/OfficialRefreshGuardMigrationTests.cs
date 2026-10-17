using System.Text.RegularExpressions;
using MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MediPOS.UnitTests.Modules.Catalog.OfficialRefresh;

public sealed class OfficialRefreshGuardMigrationTests
{
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
