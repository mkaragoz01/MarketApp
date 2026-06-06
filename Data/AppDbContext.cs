using Microsoft.EntityFrameworkCore;
using MarketApp.Models;

namespace MarketApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AppLog> AppLogs => Set<AppLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .Property(u => u.Role)
            .HasMaxLength(32)
            .HasDefaultValue(UserRoles.User);

        modelBuilder.Entity<AppLog>(entity =>
        {
            entity.Property(l => l.Level).HasMaxLength(32);
            entity.Property(l => l.Category).HasMaxLength(256);
            entity.Property(l => l.Message).HasMaxLength(2000);
            entity.Property(l => l.EventName).HasMaxLength(128);
            entity.Property(l => l.TraceId).HasMaxLength(128);
            entity.Property(l => l.Method).HasMaxLength(16);
            entity.Property(l => l.Path).HasMaxLength(512);
            entity.Property(l => l.Username).HasMaxLength(100);

            entity.HasIndex(l => l.CreatedAtUtc);
            entity.HasIndex(l => l.Level);
        });
    }
}
