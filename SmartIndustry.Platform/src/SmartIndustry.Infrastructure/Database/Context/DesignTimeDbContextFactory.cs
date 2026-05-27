// ============================================================
// 文件：DesignTimeDbContextFactory.cs
// 层次：基础设施层 (Infrastructure Layer) — 设计时工厂
// 职责：为 EF Core 迁移工具（dotnet ef migrations add / update）提供 DbContext 实例。
//       迁移工具在设计时（非运行时）需要构造 AppDbContext，
//       但此时 DI 容器尚未初始化，因此需要实现此工厂接口。
// 使用方法：
//   在 Infrastructure 项目目录执行：
//   dotnet ef migrations add InitialCreate --project SmartIndustry.Infrastructure --startup-project SmartIndustry.UI
//   dotnet ef database update --project SmartIndustry.Infrastructure --startup-project SmartIndustry.UI
// 注意：
//   此工厂使用 SQLite 内存/本地路径数据库，仅用于生成迁移文件，
//   不影响生产环境的连接字符串配置（生产配置通过 DI 注入）。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartIndustry.Infrastructure.Database.Context
{
    /// <summary>
    /// AppDbContext 设计时工厂。
    /// 实现 IDesignTimeDbContextFactory，供 EF Core CLI 迁移工具在设计时创建 DbContext。
    /// 此类在生产运行时不会被调用，仅在执行 dotnet ef 命令时使用。
    /// </summary>
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        /// <summary>
        /// 创建 AppDbContext 实例供设计时使用。
        /// </summary>
        /// <param name="args">dotnet ef 传入的命令行参数（通常为空，保留兼容性）</param>
        /// <returns>配置了 SQLite 连接的 AppDbContext 实例</returns>
        public AppDbContext CreateDbContext(string[] args)
        {
            // ----------------------------------------------------------------
            // 设计时使用固定的 SQLite 数据库路径（相对于项目目录）
            // 迁移文件生成后，生产环境通过 DI 配置实际路径
            // ----------------------------------------------------------------
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

            // 设计时数据库文件路径（放在用户临时目录，不污染工程目录）
            var designTimeDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SmartIndustry",
                "DesignTime",
                "smartindustry_design.db");

            // 确保目录存在（否则 SQLite 无法创建文件）
            var dir = Path.GetDirectoryName(designTimeDbPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // 配置 SQLite 连接
            // WAL（Write-Ahead Logging）模式：提升并发读写性能，推荐生产使用
            optionsBuilder.UseSqlite(
                $"Data Source={designTimeDbPath};Mode=ReadWriteCreate;Cache=Shared",
                sqliteOptions =>
                {
                    // 设计时不需要迁移程序集配置，使用默认即可
                });

            // 设计时启用敏感数据日志（便于调试迁移脚本）
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
