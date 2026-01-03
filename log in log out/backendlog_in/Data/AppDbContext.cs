using backendlog_in.Models;
using Microsoft.EntityFrameworkCore;

namespace backendlog_in.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Course> Courses => Set<Course>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(u => u.Email)
            .HasConversion(v => v.ToLowerInvariant(), v => v);

        base.OnModelCreating(modelBuilder);
    }
}
