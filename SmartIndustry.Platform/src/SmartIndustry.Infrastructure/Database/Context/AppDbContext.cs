// ============================================================
// 文件：AppDbContext.cs
// 层次：基础设施层 (Infrastructure Layer) — 数据库上下文
// 职责：
//   1. 提供 EF Core DbContext，配置所有实体到 SQLite 数据库的映射
//   2. 实现审计字段自动填充（CreatedAt、UpdatedAt、CreatedBy、UpdatedBy）
//   3. 实现全局软删除查询过滤器（自动排除 IsDeleted=true 的记录）
//   4. 实现乐观并发（Version 字段）
//   5. 在 OnModelCreating 中配置索引、关系、列类型
// 设计思路：
//   DbContext 是 EF Core 工作单元（Unit of Work）的核心，负责跟踪实体变更、
//   事务边界控制和 LINQ 到 SQL 的翻译。通过重写 SaveChangesAsync 在持久化
//   前统一处理审计字段，避免上层业务代码重复填写这些横切关注点。
//   软删除过滤器注册在 OnModelCreating 中，对所有继承 BaseEntity 的实体
//   自动生效，上层代码查询时完全透明（无需 .Where(x => !x.IsDeleted)）。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using Microsoft.EntityFrameworkCore;
using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Events;

namespace SmartIndustry.Infrastructure.Database.Context
{
    /// <summary>
    /// 应用程序数据库上下文。
    /// 包含工业平台所有持久化实体的 DbSet，配置 SQLite 映射规则。
    /// </summary>
    public class AppDbContext : DbContext
    {
        // ----------------------------------------------------------------
        // 构造函数：接受外部注入的 DbContextOptions（支持连接字符串配置）
        // ----------------------------------------------------------------

        /// <summary>
        /// 构造函数。接受 DI 容器注入的配置选项，支持不同环境使用不同连接字符串。
        /// </summary>
        /// <param name="options">DbContext 配置选项（连接字符串、日志等）</param>
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        // ----------------------------------------------------------------
        // 当前用户上下文（供审计字段自动填充使用）
        // 实际项目中通过 ICurrentUserService 获取，此处简化为属性注入
        // ----------------------------------------------------------------

        /// <summary>
        /// 当前操作用户名（由 Application 层在操作前设置，用于审计字段填充）。
        /// 线程不安全，建议每个请求/操作创建一个新的 DbContext 实例（Scoped 注册）
        /// </summary>
        public string? CurrentUser { get; set; }

        // ================================================================
        // DbSet 定义：每个 DbSet 对应数据库中的一张表
        // ================================================================

        /// <summary>运动轴配置表（存储运动控制卡轴参数）</summary>
        public DbSet<AxisConfig> AxisConfigs { get; set; }

        /// <summary>报警记录表（存储所有触发的报警，含活动和历史）</summary>
        public DbSet<AlarmRecord> AlarmRecords { get; set; }

        /// <summary>用户账户表（存储操作员/工程师/管理员账户信息）</summary>
        public DbSet<UserAccount> UserAccounts { get; set; }

        /// <summary>生产配方表（存储工艺参数集合，支持版本化管理）</summary>
        public DbSet<Recipe> Recipes { get; set; }

        /// <summary>通信通道配置表（存储各协议的连接参数）</summary>
        public DbSet<CommunicationChannel> CommunicationChannels { get; set; }

        /// <summary>视觉任务表（存储视觉检测配置模板和执行结果记录）</summary>
        public DbSet<VisionTask> VisionTasks { get; set; }

        /// <summary>协同会话表（存储多人协同操作的会话和共享状态）</summary>
        public DbSet<CollaborationSession> CollaborationSessions { get; set; }

        /// <summary>日志条目表（结构化日志持久化，支持历史查询）</summary>
        public DbSet<LogRecord> LogEntries { get; set; }

        // ================================================================
        // OnModelCreating：实体映射、索引、关系、全局过滤器配置
        // ================================================================

        /// <summary>
        /// EF Core 模型构建入口。在应用启动时调用一次，将 C# 实体映射到数据库 Schema。
        /// </summary>
        /// <param name="modelBuilder">模型构建器，提供 Fluent API 配置实体映射</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 排除 DomainEvent（非持久化实体，仅用于内存事件分发）
            modelBuilder.Ignore<DomainEvent>();

            // ----------------------------------------------------------------
            // 全局配置：对所有继承 BaseEntity 的实体应用软删除过滤器
            // 使用反射动态注册，避免为每个实体手动调用 HasQueryFilter
            // ----------------------------------------------------------------
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType)) continue;

                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ApplySoftDeleteFilter),
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .MakeGenericMethod(entityType.ClrType);

                method.Invoke(null, new object[] { modelBuilder });
            }

            // ----------------------------------------------------------------
            // AxisConfig 实体映射配置
            // ----------------------------------------------------------------
            modelBuilder.Entity<AxisConfig>(entity =>
            {
                entity.ToTable("AxisConfigs");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.CreatedBy).HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).HasMaxLength(100);

                // 轴索引唯一约束
                entity.HasIndex(e => e.AxisIndex)
                    .IsUnique()
                    .HasDatabaseName("IX_AxisConfigs_AxisIndex");

                entity.HasIndex(e => e.Name)
                    .HasDatabaseName("IX_AxisConfigs_Name");

                // 乐观并发令牌
                entity.Property(e => e.Version).IsConcurrencyToken();

                // 外键关系：多对一（Recipe 可为空）
                entity.HasOne(e => e.Recipe)
                    .WithMany(r => r.AxisConfigs)
                    .HasForeignKey(e => e.RecipeId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // ----------------------------------------------------------------
            // AlarmRecord 实体映射配置
            // ----------------------------------------------------------------
            modelBuilder.Entity<AlarmRecord>(entity =>
            {
                entity.ToTable("AlarmRecords");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.AlarmCode).HasMaxLength(50).IsRequired();
                entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
                entity.Property(e => e.Message).HasMaxLength(2000);
                entity.Property(e => e.Source).HasMaxLength(100);
                entity.Property(e => e.AcknowledgedBy).HasMaxLength(100);
                entity.Property(e => e.ClearedBy).HasMaxLength(100);
                entity.Property(e => e.CreatedBy).HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).HasMaxLength(100);

                // 复合索引：报警码+时间（历史查询最常见场景）
                entity.HasIndex(e => new { e.AlarmCode, e.TriggeredAt })
                    .HasDatabaseName("IX_AlarmRecords_Code_Time");

                // 活动报警快速查询索引
                entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_AlarmRecords_IsActive");
                entity.HasIndex(e => e.Level).HasDatabaseName("IX_AlarmRecords_Level");
                entity.HasIndex(e => e.TriggeredAt).HasDatabaseName("IX_AlarmRecords_TriggeredAt");

                entity.Property(e => e.Version).IsConcurrencyToken();
            });

            // ----------------------------------------------------------------
            // UserAccount 实体映射配置
            // ----------------------------------------------------------------
            modelBuilder.Entity<UserAccount>(entity =>
            {
                entity.ToTable("UserAccounts");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Username).HasMaxLength(50).IsRequired();
                entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Email).HasMaxLength(200);
                entity.Property(e => e.PasswordHash).HasMaxLength(256).IsRequired();
                entity.Property(e => e.PasswordSalt).HasMaxLength(128).IsRequired();
                entity.Property(e => e.LastLoginIp).HasMaxLength(50);
                entity.Property(e => e.CreatedBy).HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).HasMaxLength(100);

                // 用户名唯一索引（不区分大小写：SQLite COLLATE NOCASE）
                entity.HasIndex(e => e.Username)
                    .IsUnique()
                    .HasDatabaseName("IX_UserAccounts_Username");

                entity.Property(e => e.Version).IsConcurrencyToken();
            });

            // ----------------------------------------------------------------
            // Recipe 实体映射配置
            // ----------------------------------------------------------------
            modelBuilder.Entity<Recipe>(entity =>
            {
                entity.ToTable("Recipes");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.RecipeName).HasMaxLength(200).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(1000);
                entity.Property(e => e.CreatedBy).HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).HasMaxLength(100);

                // 联合唯一索引：配方名+版本号唯一
                entity.HasIndex(e => new { e.RecipeName, e.Version })
                    .IsUnique()
                    .HasDatabaseName("IX_Recipes_Name_Version");

                entity.HasIndex(e => e.State).HasDatabaseName("IX_Recipes_State");

                entity.Property(e => e.Version).IsConcurrencyToken();
            });

            // ----------------------------------------------------------------
            // CommunicationChannel 实体映射配置
            // ----------------------------------------------------------------
            modelBuilder.Entity<CommunicationChannel>(entity =>
            {
                entity.ToTable("CommunicationChannels");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ChannelName).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.HostAddress).HasMaxLength(255);
                entity.Property(e => e.SerialPortName).HasMaxLength(50);
                entity.Property(e => e.CreatedBy).HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).HasMaxLength(100);

                // 通道名唯一索引（事件路由按名称定位）
                entity.HasIndex(e => e.ChannelName)
                    .IsUnique()
                    .HasDatabaseName("IX_CommunicationChannels_Name");

                entity.Property(e => e.Version).IsConcurrencyToken();
            });

            // ----------------------------------------------------------------
            // VisionTask 实体映射配置
            // ----------------------------------------------------------------
            modelBuilder.Entity<VisionTask>(entity =>
            {
                entity.ToTable("VisionTasks");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.TaskName).HasMaxLength(200).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.CameraId).HasMaxLength(100);
                entity.Property(e => e.SourceImagePath).HasMaxLength(500);
                entity.Property(e => e.ResultImagePath).HasMaxLength(500);
                entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
                entity.Property(e => e.CreatedBy).HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).HasMaxLength(100);

                entity.HasIndex(e => e.TaskType).HasDatabaseName("IX_VisionTasks_TaskType");
                entity.HasIndex(e => e.IsTemplate).HasDatabaseName("IX_VisionTasks_IsTemplate");
                entity.HasIndex(e => e.ExecutedAt).HasDatabaseName("IX_VisionTasks_ExecutedAt");

                entity.Property(e => e.Version).IsConcurrencyToken();
            });

            // ----------------------------------------------------------------
            // CollaborationSession 实体映射配置
            // ----------------------------------------------------------------
            modelBuilder.Entity<CollaborationSession>(entity =>
            {
                entity.ToTable("CollaborationSessions");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.SessionName).HasMaxLength(200).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.OwnerUsername).HasMaxLength(100);
                entity.Property(e => e.CreatedBy).HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).HasMaxLength(100);

                entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_CollaborationSessions_IsActive");
                entity.HasIndex(e => e.OwnerId).HasDatabaseName("IX_CollaborationSessions_OwnerId");

                entity.Property(e => e.Version).IsConcurrencyToken();
            });

            // ----------------------------------------------------------------
            // LogEntry 实体映射配置
            // ----------------------------------------------------------------
            modelBuilder.Entity<LogRecord>(entity =>
            {
                entity.ToTable("LogEntries");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Message).HasMaxLength(2000).IsRequired();
                entity.Property(e => e.Source).HasMaxLength(200);
                entity.Property(e => e.ExceptionType).HasMaxLength(500);
                entity.Property(e => e.ExceptionMessage).HasMaxLength(2000);
                entity.Property(e => e.Username).HasMaxLength(100);
                entity.Property(e => e.CreatedBy).HasMaxLength(100);
                entity.Property(e => e.UpdatedBy).HasMaxLength(100);

                entity.HasIndex(e => e.Level).HasDatabaseName("IX_LogEntries_Level");
                entity.HasIndex(e => e.Timestamp).HasDatabaseName("IX_LogEntries_Timestamp");
                entity.HasIndex(e => e.Source).HasDatabaseName("IX_LogEntries_Source");

                entity.Property(e => e.Version).IsConcurrencyToken();
            });
        }

        // ================================================================
        // 私有辅助方法：动态应用软删除全局过滤器
        // ================================================================

        /// <summary>
        /// 为指定实体类型应用软删除全局查询过滤器（通过反射被 OnModelCreating 动态调用）。
        /// 注册后，每次 LINQ 查询此实体类型时，EF Core 自动追加 WHERE IsDeleted = 0。
        /// </summary>
        private static void ApplySoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
            where TEntity : BaseEntity
        {
            modelBuilder.Entity<TEntity>()
                .HasQueryFilter(e => !e.IsDeleted);
        }

        // ================================================================
        // SaveChangesAsync 重写：审计字段自动填充
        // ================================================================

        /// <summary>
        /// 重写 SaveChangesAsync，在提交事务前自动填充审计字段。
        /// 拦截所有 Added/Modified/Deleted 状态的 BaseEntity 子类实体，统一处理。
        /// </summary>
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            FillAuditFields();
            return await base.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// 同步版本：保证同步调用路径也触发审计字段填充
        /// </summary>
        public override int SaveChanges()
        {
            FillAuditFields();
            return base.SaveChanges();
        }

        /// <summary>
        /// 遍历变更跟踪器，按实体状态填充审计字段：
        /// - Added：设置 CreatedAt/UpdatedAt/CreatedBy/UpdatedBy，Version=1
        /// - Modified：更新 UpdatedAt/UpdatedBy，递增 Version，保护 CreatedAt 不被修改
        /// - Deleted：拦截物理删除，转换为软删除（IsDeleted=true）
        /// </summary>
        private void FillAuditFields()
        {
            var now = DateTime.UtcNow;

            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        // 新增：强制使用服务器时间（防止客户端篡改）
                        entry.Entity.CreatedAt = now;
                        entry.Entity.UpdatedAt = now;
                        if (string.IsNullOrEmpty(entry.Entity.CreatedBy))
                            entry.Entity.CreatedBy = CurrentUser;
                        if (string.IsNullOrEmpty(entry.Entity.UpdatedBy))
                            entry.Entity.UpdatedBy = CurrentUser;
                        // byte[] Version 由 EF Core 自动管理（SQLite 使用触发器或手动赋初始值均可）
                        // 此处不手动赋值，EF Core 会在 INSERT 后从数据库刷新该字段
                        entry.Entity.IsDeleted = false;
                        break;

                    case EntityState.Modified:
                        // 修改：只更新修改相关字段，保护创建字段不变
                        entry.Entity.UpdatedAt = now;
                        if (!string.IsNullOrEmpty(CurrentUser))
                            entry.Entity.UpdatedBy = CurrentUser;
                        // byte[] Version 由 EF Core 并发控制机制自动管理，不手动递增
                        // 明确标记创建字段为未修改，防止 EF Core 生成不必要的 UPDATE SET CreatedAt=...
                        entry.Property(e => e.CreatedAt).IsModified = false;
                        entry.Property(e => e.CreatedBy).IsModified = false;
                        break;

                    case EntityState.Deleted:
                        // 拦截物理删除：转为软删除
                        // 这是防御性编程：即使调用方误用 context.Remove()，也不会真正删除数据
                        entry.State = EntityState.Modified;
                        entry.Entity.IsDeleted = true;
                        entry.Entity.UpdatedAt = now;
                        entry.Entity.UpdatedBy = CurrentUser ?? "System";
                        // byte[] Version 由 EF Core 并发控制机制自动管理，不手动递增
                        break;
                }
            }
        }
    }
}
