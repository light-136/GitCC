// ============================================================
// 文件：AlarmRecord.cs
// 层次：领域层 (Domain Layer) — 实体
// 职责：表示一条报警记录，包含报警触发、确认、消除的完整生命周期
// 设计思路：
//   报警记录是工业平台中最重要的审计数据之一。
//   生命周期：Triggered（触发）→ Acknowledged（已确认）→ Cleared（已消除）
//   IsActive 字段驱动"活动报警"查询，无需复杂状态计算。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Entities
{
    /// <summary>
    /// 报警记录实体，对应数据库表 AlarmRecords。
    /// 记录从报警触发到消除的完整生命周期数据，用于操作追溯和统计分析。
    /// </summary>
    public class AlarmRecord : BaseEntity
    {
        // ----------------------------------------------------------------
        // 报警分类属性
        // ----------------------------------------------------------------

        /// <summary>
        /// 报警代码（格式：[模块前缀]-[数字编号]，如 "MOT-001"、"VIS-101"）
        /// 用于自动化处理和文档检索，必须唯一且稳定（不随语言变化）
        /// </summary>
        public string AlarmCode { get; set; } = string.Empty;

        /// <summary>报警等级（Info/Warning/Error/Critical/Fatal），影响处理优先级</summary>
        public AlarmLevel Level { get; set; } = AlarmLevel.Warning;

        /// <summary>报警类别（Motion/Vision/Communication/Safety/System/Process）</summary>
        public AlarmCategory Category { get; set; } = AlarmCategory.System;

        // ----------------------------------------------------------------
        // 报警内容属性
        // ----------------------------------------------------------------

        /// <summary>报警标题（简短描述，用于列表展示，建议不超过50字）</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 报警详细描述（包含故障现象、可能原因、建议处理步骤）
        /// 支持 Markdown 格式，UI层负责渲染
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 报警来源模块名称（如"运动控制模块"、"视觉引擎"），
        /// 便于维护人员快速定位问题所在子系统
        /// </summary>
        public string Source { get; set; } = string.Empty;

        // ----------------------------------------------------------------
        // 报警生命周期状态
        // ----------------------------------------------------------------

        /// <summary>
        /// 是否为活动报警（true=未消除，false=已消除）。
        /// 作为查询索引字段，避免复杂的状态计算，活动报警 = IsActive AND NOT IsDeleted
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>报警触发时间（UTC）</summary>
        public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 报警确认时间（UTC，null=尚未确认）
        /// 操作员确认表示"已知晓此报警"，不代表已处理
        /// </summary>
        public DateTime? AcknowledgedAt { get; set; }

        /// <summary>确认操作人用户名（null=尚未确认）</summary>
        public string? AcknowledgedBy { get; set; }

        /// <summary>
        /// 报警消除时间（UTC，null=尚未消除）
        /// 消除表示触发条件已解除，IsActive 同步置为 false
        /// </summary>
        public DateTime? ClearedAt { get; set; }

        /// <summary>消除操作人（null=系统自动消除，非null=人工确认消除）</summary>
        public string? ClearedBy { get; set; }

        // ----------------------------------------------------------------
        // 附加信息
        // ----------------------------------------------------------------

        /// <summary>
        /// 附加上下文数据（JSON 格式），存储触发报警时的现场快照。
        /// 如：轴位置、电流值、图像路径、通信帧内容等
        /// </summary>
        public string? AdditionalData { get; set; }

        // ----------------------------------------------------------------
        // 领域行为方法
        // ----------------------------------------------------------------

        /// <summary>
        /// 确认报警：记录确认时间和操作人。
        /// 已确认的报警再次调用此方法为幂等操作（不抛异常，不修改）。
        /// </summary>
        /// <param name="acknowledgedBy">执行确认操作的用户名</param>
        public void Acknowledge(string acknowledgedBy)
        {
            // 幂等：已确认则跳过
            if (AcknowledgedAt.HasValue) return;

            AcknowledgedAt = DateTime.UtcNow;
            AcknowledgedBy = acknowledgedBy;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = acknowledgedBy;
        }

        /// <summary>
        /// 消除报警：将 IsActive 置为 false，记录消除时间和操作人。
        /// 已消除的报警再次调用为幂等操作。
        /// </summary>
        /// <param name="clearedBy">执行消除操作的用户名（null表示系统自动消除）</param>
        public void Clear(string? clearedBy = null)
        {
            // 幂等：已消除则跳过
            if (!IsActive) return;

            IsActive = false;
            ClearedAt = DateTime.UtcNow;
            ClearedBy = clearedBy;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = clearedBy ?? "System";
        }
    }
}
