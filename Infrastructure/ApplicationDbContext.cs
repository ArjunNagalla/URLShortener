using Microsoft.EntityFrameworkCore;
using ShortUrl.Api.Domain;

namespace ShortUrl.Api.Infrastructure;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Link> Links => Set<Link>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Link>(entity =>
        {
            entity.HasKey(e => e.Id);

            var codeProperty = entity.Property(e => e.Code)
                .IsRequired()
                .HasMaxLength(32);

            if (Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                codeProperty.UseCollation("C");
            }

            entity.HasIndex(e => e.Code)
                .IsUnique();

            entity.Property(e => e.DestinationUrl)
                .IsRequired()
                .HasMaxLength(2048);

            entity.Property(e => e.CreatedAtUtc)
                .IsRequired();

            entity.Property(e => e.TotalClicks)
                .IsRequired();

            var customAliasProperty = entity.Property(e => e.CustomAlias)
                .HasMaxLength(32);

            if (Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            {
                customAliasProperty.UseCollation("C");
            }

            entity.HasIndex(e => e.CustomAlias)
                .IsUnique();
        });
    }
}
