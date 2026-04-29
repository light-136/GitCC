using Microsoft.EntityFrameworkCore;

namespace SmartMES.Modules.Database
{
    // ============================================================
    // 数据库实体定义
    // ============================================================

    /// <summary>生产记录实体</summary>
    public class ProductionRecord
    {
        public int Id { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public int Qty { get; set; }
        public bool IsPass { get; set; }
        public double Temperature { get; set; }
        public double Pressure { get; set; }
        public DateTime RecordTime { get; set; } = DateTime.Now;
        public string Operator { get; set; } = string.Empty;
    }

    /// <summary>设备日志实体</summary>
    public class DeviceLog
    {
        public int Id { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime LogTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// EF Core DbContext - 统一数据库上下文
    /// 通过传入不同options支持SQLite/MySQL/SqlServer切换
    /// Code First：先写实体类，EF自动生成建表SQL
    /// </summary>
    public class SmartMesDbContext : DbContext
    {
        public DbSet<ProductionRecord> ProductionRecords => Set<ProductionRecord>();
        public DbSet<DeviceLog> DeviceLogs => Set<DeviceLog>();

        /// <summary>
        /// 自动补齐：SmartMesDbContext 方法说明。
        /// </summary>
        public SmartMesDbContext(DbContextOptions<SmartMesDbContext> options)
            : base(options) { }

        /// <summary>
        /// 自动补齐：OnModelCreating 方法说明。
        /// </summary>
        protected override void OnModelCreating(ModelBuilder mb)
        {
            // 生产记录表配置
            mb.Entity<ProductionRecord>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.OrderId).HasMaxLength(50).IsRequired();
                e.Property(x => x.ProductCode).HasMaxLength(50);
                e.Property(x => x.Operator).HasMaxLength(50);
                e.HasIndex(x => x.OrderId);
                e.HasIndex(x => x.RecordTime);
            });

            // 设备日志表配置
            mb.Entity<DeviceLog>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.DeviceName).HasMaxLength(100);
                e.Property(x => x.EventType).HasMaxLength(50);
                e.HasIndex(x => x.LogTime);
            });
        }
    }

    // ============================================================
    // 数据库工厂 - 根据配置类型创建不同数据库的DbContext
    // ============================================================
    public enum DbType { SQLite, MySQL, SqlServer }

    public static class DbContextFactory
    {
        /// <summary>
        /// 创建指定类型的DbContext
        /// SQLite：无需安装服务，适合嵌入式/单机场景
        /// MySQL：开源，适合Linux服务器部署
        /// SqlServer：企业级，适合Windows生产环境
        /// </summary>
        public static SmartMesDbContext Create(DbType dbType, string connectionString)
        {
            var builder = new DbContextOptionsBuilder<SmartMesDbContext>();
            switch (dbType)
            {
                case DbType.SQLite:
                    builder.UseSqlite(connectionString);
                    break;
                case DbType.MySQL:
                    // Pomelo驱动支持MySQL 5.7+ 和 MariaDB
                    builder.UseMySql(connectionString,
                        ServerVersion.AutoDetect(connectionString));
                    break;
                case DbType.SqlServer:
                    builder.UseSqlServer(connectionString);
                    break;
            }
            builder.LogTo(msg => System.Diagnostics.Debug.WriteLine(msg),
                Microsoft.Extensions.Logging.LogLevel.Information);
            return new SmartMesDbContext(builder.Options);
        }
    }
}
