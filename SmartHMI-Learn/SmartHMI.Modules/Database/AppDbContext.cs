using Microsoft.EntityFrameworkCore;
using SmartHMI.Core.Models;

namespace SmartHMI.Modules.Database;

public class AppDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<AlarmRecord> AlarmRecords => Set<AlarmRecord>();
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();
    public DbSet<DeviceModel> Devices => Set<DeviceModel>();

    public AppDbContext(string dbPath = "smartHMI.db")
    {
        _dbPath = dbPath;
        Database.EnsureCreated();
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={_dbPath}");

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AlarmRecord>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Level).HasConversion<string>();
        });

        model.Entity<LogEntry>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Level).HasConversion<string>();
        });

        model.Entity<DeviceModel>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Type).HasConversion<string>();
            e.Property(d => d.Status).HasConversion<string>();
            e.Ignore(d => d.Properties);
        });
    }
}
