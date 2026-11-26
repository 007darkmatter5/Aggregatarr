using Microsoft.EntityFrameworkCore;

namespace Aggregatarr.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<ServiceEntity> Services => Set<ServiceEntity>();
    public DbSet<ActionLogEntity> ActionLogs => Set<ActionLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Username).HasMaxLength(32);
        });

        modelBuilder.Entity<ServiceEntity>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Kind).HasConversion<string>().HasMaxLength(32);
            entity.Property(s => s.Name).HasMaxLength(80);
            entity.Property(s => s.BaseUrl).HasMaxLength(300);
            entity.Property(s => s.Username).HasMaxLength(120);
        });

        modelBuilder.Entity<ActionLogEntity>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Action).HasMaxLength(40);
            entity.Property(a => a.Username).HasMaxLength(32);
            entity.Property(a => a.ServiceName).HasMaxLength(80);
            entity.Property(a => a.Target).HasMaxLength(300);
            entity.Property(a => a.Detail).HasMaxLength(500);
            entity.HasIndex(a => a.At);
        });
    }
}
