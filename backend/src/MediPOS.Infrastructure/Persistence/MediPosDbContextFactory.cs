using MediPOS.Application.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MediPOS.Infrastructure.Persistence;

public sealed class MediPosDbContextFactory : IDesignTimeDbContextFactory<MediPosDbContext>
{
    public MediPosDbContext CreateDbContext(string[] args)
    {
        // Provider-only configuration allows migration scaffolding without a database or credentials.
        var options = new DbContextOptionsBuilder<MediPosDbContext>().UseNpgsql();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MediPosDatabase");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            options.UseNpgsql(connectionString);
        }

        return new MediPosDbContext(options.Options, new TenantDataContext());
    }
}
