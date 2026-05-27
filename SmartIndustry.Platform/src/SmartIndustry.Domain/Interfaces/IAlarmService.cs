// ============================================================
// 文件：IAlarmService.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义报警管理服务接口（触发/确认/清除/查询报警）
// 设计思路：
//   IAlarmService 是平台报警子系统的核心服务接口，集中管理所有报警的
//   生命周期（触发 -> 确认 -> 清除）和历史查询。
//   报警定义（AlarmDefinition）机制：
//     RegisterAlarmDefinition 预先注册已知报警类型，包含默认等级、
//     类别和描述模板，使触发报警时不必每次传递所有参数。
//   与领域事件的关系：
//     TriggerAlarm 内部创建 AlarmRecord 实体并发布 AlarmTriggeredEvent，
//     调用方无需手动发布事件，IAlarmService 统一负责。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 报警管理服务接口。
    /// 提供报警的完整生命周期管理：触发、确认、清除和历史查询。
    /// </summary>
    public interface IAlarmService
    {
        // ----------------------------------------------------------------
        // 报警触发与管理
        // ----------------------------------------------------------------

        /// <summary>
        /// 触发一条新报警（创建 AlarmRecord 实体并发布领域事件）。
        /// 若相同 alarmCode 的报警已处于活动状态，则递增 RepeatCount 而非创建新记录。
        /// </summary>
        /// <param name="alarmCode">报警编码（如 "MOT-001"）</param>
        /// <param name="source">报警来源模块名称</param>
        /// <param name="message">报警简要信息</param>
        /// <param name="detail">报警详细信息（可为 null）</param>
        /// <param name="level">
        /// 报警等级（null 时使用已注册的定义默认值，未注册则默认 Warning）
        /// </param>
        /// <param name="category">
        /// 报警类别（null 时使用已注册的定义默认值，未注册则默认 System）
        /// </param>
        /// <returns>创建或更新的报警记录实体</returns>
        Task<AlarmRecord> TriggerAlarm(
            string alarmCode,
            string source,
            string message,
            string? detail = null,
            AlarmLevel? level = null,
            AlarmCategory? category = null);

        /// <summary>
        /// 操作员确认报警（标记为已知晓，报警可能仍处于活动状态）。
        /// </summary>
        /// <param name="alarmId">报警记录 ID</param>
        /// <param name="acknowledgedBy">确认操作人用户名</param>
        Task AcknowledgeAlarm(Guid alarmId, string acknowledgedBy);

        /// <summary>
        /// 清除报警（故障根因消除后调用，IsActive 置为 false）。
        /// </summary>
        /// <param name="alarmId">报警记录 ID</param>
        /// <param name="clearedBy">清除操作人（"SYSTEM" 表示系统自动清除）</param>
        Task ClearAlarm(Guid alarmId, string clearedBy);

        /// <summary>
        /// 按报警编码清除所有活动报警（批量清除，用于故障恢复场景）。
        /// </summary>
        /// <param name="alarmCode">报警编码</param>
        /// <param name="clearedBy">清除操作人</param>
        Task ClearAlarmByCode(string alarmCode, string clearedBy);

        // ----------------------------------------------------------------
        // 报警查询
        // ----------------------------------------------------------------

        /// <summary>
        /// 获取所有活动报警（IsActive=true 的报警列表，按等级和时间排序）。
        /// </summary>
        Task<IReadOnlyList<AlarmRecord>> GetActiveAlarms();

        /// <summary>
        /// 分页查询历史报警记录。
        /// </summary>
        /// <param name="startTime">查询开始时间（UTC）</param>
        /// <param name="endTime">查询结束时间（UTC）</param>
        /// <param name="level">过滤报警等级（null 表示不过滤）</param>
        /// <param name="category">过滤报警类别（null 表示不过滤）</param>
        /// <param name="pageIndex">页码（0-based）</param>
        /// <param name="pageSize">每页条数（默认 50）</param>
        Task<(IReadOnlyList<AlarmRecord> Items, int TotalCount)> GetAlarmHistory(
            DateTime startTime,
            DateTime endTime,
            AlarmLevel? level = null,
            AlarmCategory? category = null,
            int pageIndex = 0,
            int pageSize = 50);

        // ----------------------------------------------------------------
        // 报警定义注册（预配置已知报警类型）
        // ----------------------------------------------------------------

        /// <summary>
        /// 注册报警定义（预先注册已知报警的元数据，简化后续触发调用）。
        /// 重复注册同一 alarmCode 时覆盖已有定义（幂等操作）。
        /// </summary>
        /// <param name="alarmCode">报警编码（唯一标识）</param>
        /// <param name="defaultLevel">默认等级</param>
        /// <param name="defaultCategory">默认类别</param>
        /// <param name="descriptionTemplate">
        /// 描述模板（支持占位符，如 "轴 {AxisName} 位置超差 {Deviation:F2} mm"）
        /// </param>
        void RegisterAlarmDefinition(
            string alarmCode,
            AlarmLevel defaultLevel,
            AlarmCategory defaultCategory,
            string descriptionTemplate);
    }
}
