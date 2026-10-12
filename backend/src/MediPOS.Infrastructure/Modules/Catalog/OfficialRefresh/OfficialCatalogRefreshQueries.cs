using MediPOS.Application.Modules.Catalog.OfficialRefresh;
using MediPOS.Domain.Modules.Catalog.OfficialRefresh;

namespace MediPOS.Infrastructure.Modules.Catalog.OfficialRefresh;

public static class OfficialCatalogRefreshQueries
{
    public static IQueryable<OfficialCatalogRefresh> Queue(IQueryable<OfficialCatalogRefresh> query, GetOfficialCatalogRefreshQueueCommand command)
    {
        if (command.SourceCode is not null) query = query.Where(value => value.SourceCode == command.SourceCode);
        if (command.SourceVersion is not null) query = query.Where(value => value.SourceVersion == command.SourceVersion);
        if (command.Status is not null) query = query.Where(value => value.Status == command.Status);
        if (command.Classification is not null) query = query.Where(value => value.ChangeClassification == command.Classification);
        if (command.GlobalProductId.HasValue) query = query.Where(value => value.GlobalProductId == command.GlobalProductId);
        if (command.FromUtc.HasValue) query = query.Where(value => value.CreatedAtUtc >= command.FromUtc);
        if (command.ToUtc.HasValue) query = query.Where(value => value.CreatedAtUtc <= command.ToUtc);
        return query.OrderBy(value => value.CreatedAtUtc).ThenBy(value => value.Id).Skip(command.Offset).Take(command.Limit + 1);
    }
    public static IQueryable<OfficialCatalogVersionHistory> History(IQueryable<OfficialCatalogVersionHistory> query, GetOfficialCatalogRefreshHistoryCommand command)
    {
        if (command.RefreshId.HasValue) query = query.Where(value => value.RefreshId == command.RefreshId);
        if (command.GlobalProductId.HasValue) query = query.Where(value => value.GlobalProductId == command.GlobalProductId);
        if (command.SourceCode is not null) query = query.Where(value => value.SourceCode == command.SourceCode);
        if (command.SourceVersion is not null) query = query.Where(value => value.SourceVersion == command.SourceVersion);
        return query.OrderBy(value => value.AtUtc).ThenBy(value => value.Id).Skip(command.Offset).Take(command.Limit + 1);
    }
}
