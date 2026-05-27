// ============================================================
// 文件：AlarmRepository.cs
// 层次：基础设施层 (Infrastructure Layer) — 报警仓储实现
// 职责：
//   继承 GenericRepository<AlarmRecord>，实现 IAlarmRepository 定义的
//   报警专用查询：活动报警、时间范围查询、统计分析、批量确认。
// 设计思路：
//   专用仓储（Specific Repository）在泛型仓储的 CRUD 基础上添加业务特定查询。
//   报警查询是平台最高频的 UI 操作（每秒刷新活动报警面板），
//   因此 GetActiveAlarmsAsync 专门优化：使用覆盖索引字段 IsActive，
//   排序按等级降序+时间降序，保证最严重的最新报警排在最前面。
//   GetAlarmStatistics 使用 EF Core 的 GroupBy 翻译为 SQL GROUP BY，
//   避免将大量记录加载到内存后再统计。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using Microsoft.EntityFrameworkCore;
using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.Interfaces.Repositories;
using SmartIndustry.Infrastructure.Database.Context;

namespace SmartIndustry.Infrastructure.Database.Repositories
{
    /// <summary>
    /// 报警记录专用仓储，实现 IAlarmRepository 定义的业务查询方法。
    /// </summary>
    public class AlarmRepository : GenericRepository<AlarmRecord>, IAlarmRepository
    {
        /// <summary>
        /// 构造函数：注入 AppDbContext
        /// </summary>
        public AlarmRepository(AppDbContext context) : base(context)
        {
        }

        // ================================================================
        // 报警专用查询实现
        // ================================================================

        /// <summary>
        /// 查询所有当前活动（IsActive=true）的报警记录。
        /// 排序策略：先按等级降序（Critical/Fatal 优先），再按触发时间降序（最新优先）。
        /// 此查询命中 IX_AlarmRecords_IsActive 索引，在报警少量时性能极佳。
        /// </summary>
        public async Task<IReadOnlyList<AlarmRecord>> GetActiveAlarmsAsync(
            CancellationToken cancellationToken = default)
        {
            return await _dbSet
                .Where(a => a.IsActive)                           // 仅活动报警（命中索引）
                .OrderByDescending(a => a.Level)                  // 严重等级高的排前面
                .ThenByDescending(a => a.TriggeredAt)             // 等级相同则最新的排前面
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// 按时间范围查询历史报警记录（含已消除的报警）。
        /// 支持按类别和最低等级进行二次过滤，所有条件均在数据库层面执行。
        /// </summary>
        /// <param name="from">查询起始时间（UTC，含边界）</param>
        /// <param name="to">查询结束时间（UTC，含边界）</param>
        /// <param name="category">报警类别过滤（null=不限制类别）</param>
        /// <param name="minLevel">最低等级过滤（null=不限制等级，包含所有等级）</param>
        public async Task<IReadOnlyList<AlarmRecord>> GetAlarmsByDateRangeAsync(
            DateTime from,
            DateTime to,
            AlarmCategory? category = null,
            AlarmLevel? minLevel = null,
            CancellationToken cancellationToken = default)
        {
            // 构建基础查询（时间范围，命中 IX_AlarmRecords_TriggeredAt 索引）
            var query = _dbSet
                .Where(a => a.TriggeredAt >= from && a.TriggeredAt <= to);

            // 可选：按类别过滤
            if (category.HasValue)
                query = query.Where(a => a.Category == category.Value);

            // 可选：按最低等级过滤（枚举值比较：>= minLevel 即包含此级别及以上）
            if (minLevel.HasValue)
                query = query.Where(a => a.Level >= minLevel.Value);

            return await query
                .OrderByDescending(a => a.TriggeredAt)
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// 按类别和等级统计报警次数（SQL GROUP BY 实现，不加载记录到内存）。
        /// 返回字典：Key = (Category, Level) 元组，Value = 该组合的报警触发次数。
        /// 报表模块使用此方法生成报警热力图和趋势分析图。
        /// </summary>
        public async Task<Dictionary<(AlarmCategory Category, AlarmLevel Level), int>> GetAlarmStatisticsAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
        {
            // EF Core 将此 GroupBy 翻译为：
            // SELECT Category, Level, COUNT(*) FROM AlarmRecords
            // WHERE TriggeredAt >= @from AND TriggeredAt <= @to
            // GROUP BY Category, Level
            var groups = await _dbSet
                .Where(a => a.TriggeredAt >= from && a.TriggeredAt <= to)
                .GroupBy(a => new { a.Category, a.Level })
                .Select(g => new
                {
                    g.Key.Category,
                    g.Key.Level,
                    Count = g.Count()
                })
                .ToListAsync(cancellationToken);

            // 将查询结果转换为 Dictionary
            return groups.ToDictionary(
                g => (g.Category, g.Level),
                g => g.Count);
        }

        /// <summary>
        /// 批量确认指定类别（或全部）的活动报警。
        /// 使用 ExecuteUpdateAsync（EF Core 7+ 的批量更新 API）直接生成 SQL UPDATE，
        /// 避免逐条加载后逐条修改（N+1 性能问题）。
        /// </summary>
        /// <param name="acknowledgedBy">确认操作人用户名</param>
        /// <param name="category">要确认的类别（null=确认所有类别的活动报警）</param>
        public async Task AcknowledgeAllActiveAsync(
            string acknowledgedBy,
            AlarmCategory? category = null,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            // 构建目标查询：所有活动且尚未确认的报警
            var query = _dbSet
                .Where(a => a.IsActive && !a.AcknowledgedAt.HasValue);

            if (category.HasValue)
                query = query.Where(a => a.Category == category.Value);

            // EF Core 8 批量更新（生成单条 UPDATE SQL，效率远高于逐条更新）
            // 注意：此方法绕过变更跟踪器，不会触发 AppDbContext.SaveChanges 的审计逻辑
            //      因此需要在此处手动设置 UpdatedAt 和 UpdatedBy
            await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(a => a.AcknowledgedAt, now)
                    .SetProperty(a => a.AcknowledgedBy, acknowledgedBy)
                    .SetProperty(a => a.UpdatedAt, now)
                    .SetProperty(a => a.UpdatedBy, acknowledgedBy),
                cancellationToken);
        }
    }
}
