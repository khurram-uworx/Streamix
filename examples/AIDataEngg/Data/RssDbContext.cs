using AIDataEngg.Models;
using Microsoft.EntityFrameworkCore;

namespace AIDataEngg.Data;

public class RssDbContext : DbContext
{
    public RssDbContext()
    {
    }

    public RssDbContext(DbContextOptions<RssDbContext> options)
        : base(options)
    {
    }

    public DbSet<RssItem> RssItems => Set<RssItem>();
    public DbSet<ClassifiedRssItem> Classifications => Set<ClassifiedRssItem>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder.UseSqlite("Data Source=aidataengg.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RssItem>(entity =>
        {
            entity.HasIndex(e => e.ContentHash).IsUnique();
            entity.HasIndex(e => e.Processed);
        });

        modelBuilder.Entity<ClassifiedRssItem>(entity =>
        {
            entity.HasOne(e => e.RssItem)
                  .WithMany()
                  .HasForeignKey(e => e.RssItemId);
        });
    }
}
