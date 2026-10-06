using Microsoft.EntityFrameworkCore;

namespace MediPOS.Infrastructure.Persistence;

public sealed class MediPosDbContext(DbContextOptions<MediPosDbContext> options) : DbContext(options);
