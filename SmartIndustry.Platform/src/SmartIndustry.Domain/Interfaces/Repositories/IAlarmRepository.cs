// ============================================================
// 文件：IAlarmRepository.cs
// 层次：领域层 (Domain Layer) — 报警仓储接口
// 职责：扩展泛型仓储，添加报警业务特定查询契约
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Interfaces.Repositories
{
    /// <summary>
    /// 报警记录专用仓储接口，在泛型 CRUD 基础上添加报警业务查询。
    /// </summary>
    public interface IAlarmRepository : IRepository<AlarmRecord>
    {
        /// <summary>
        /// 查询所有当前活动（未消除）的报警，按等级和时间降序排列。
        /// 供报警面板实时展示使用。
        /// </summary>
        Task<IReadOnlyList<AlarmRecord>> GetActiveAlarmsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 按时间范围查询历史报警记录（含已消除的）。
        /// </summary>
        /// <param name="from">开始时间（UTC，含）</param>
        /// <param name="to">结束时间（UTC，含）</param>
        /// <param name="category">报警类别过滤（null=不过滤）</param>
        /// <param name="level">最低报警等级过滤（null=不过滤）</param>
        Task<IReadOnlyList<AlarmRecord>> GetAlarmsByDateRangeAsync(
            DateTime from,
            DateTime to,
            AlarmCategory? category = null,
            AlarmLevel? minLevel = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取指定时间段内的报警统计数据（按类别和等级分组计数）。
        /// 供报表模块生成报警分析图表使用。
        /// </summary>
        /// <param name="from">统计起始时间（UTC）</param>
        /// <param name="to">统计结束时间（UTC）</param>
        /// <returns>
        /// 统计结果字典，Key = (Category, Level) 元组，Value = 报警次数
        /// </returns>
        Task<Dictionary<(AlarmCategory Category, AlarmLevel Level), int>> GetAlarmStatisticsAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量确认指定类别的所有活动报警（如：一键确认所有 Motion 类报警）。
        /// </summary>
        Task AcknowledgeAllActiveAsync(string acknowledgedBy, AlarmCategory? category = null,
            CancellationToken cancellationToken = default);
    }
}
