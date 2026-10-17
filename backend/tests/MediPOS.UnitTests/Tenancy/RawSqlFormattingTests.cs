using MediPOS.Application.Tenancy;
using MediPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediPOS.UnitTests.Tenancy;

public sealed class RawSqlFormattingTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"value\":1}")]
    public void LiteralJsonBracesMustSurviveEfCompositeFormatting(string json)
    {
        using var context = new MediPosDbContext(new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql().Options, new TenantDataContext());
        var builder = context.GetService<IRawSqlCommandBuilder>();
        var sql = "UPDATE sync_events SET payload='" + json + "'";
        Assert.Throws<FormatException>(() => builder.Build(sql, [], context.Model));
        var escaped = sql.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);
        Assert.Equal(sql, builder.Build(escaped, [], context.Model).RelationalCommand.CommandText);
    }
}
