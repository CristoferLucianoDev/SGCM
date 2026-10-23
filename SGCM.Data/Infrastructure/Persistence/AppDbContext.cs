using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Infrastructure.Persistence;

internal sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Specialty> Specialties => Set<Specialty>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        var normalizeText = modelBuilder.HasDbFunction(() => PostgresFunctions.NormalizeText(default))
            .HasName("sp_text_normalize")
            .HasSchema("people")
            .HasStoreType("text")
            .IsNullable(true);

        normalizeText.HasParameter("value").HasStoreType("text").PropagatesNullability(true);
    }
}
