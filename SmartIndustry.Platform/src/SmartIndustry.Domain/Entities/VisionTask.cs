// ============================================================
// 文件：VisionTask.cs
// 层次：领域层 (Domain Layer) — 实体
// 职责：表示一次视觉检测任务的配置和执行结果记录
// 设计思路：
//   VisionTask 同时承担两个职责：
//   1. 任务配置（持久化的检测参数模板）
//   2. 任务执行记录（每次执行的结果快照）
//   通过 IsTemplate 字段区分：Template=true 为配置模板，false 为执行记录。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Entities
{
    /// <summary>
    /// 视觉检测任务实体，对应数据库表 VisionTasks。
    /// 存储视觉检测的配置参数和每次执行的结果数据。
    /// </summary>
    public class VisionTask : BaseEntity
    {
        // ----------------------------------------------------------------
        // 任务标识
        // ----------------------------------------------------------------

        /// <summary>任务名称（如："晶圆外观检测"、"Mark点定位"）</summary>
        public string TaskName { get; set; } = string.Empty;

        /// <summary>任务描述（说明检测目的和检测项目）</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>视觉任务类型（决定视觉引擎选用的算法）</summary>
        public VisionTaskType TaskType { get; set; } = VisionTaskType.PatternMatch;

        /// <summary>
        /// 是否为配置模板（true=作为模板供执行记录引用，false=单次执行结果记录）
        /// </summary>
        public bool IsTemplate { get; set; } = true;

        // ----------------------------------------------------------------
        // 配置参数（模板有效，执行记录复制模板参数快照）
        // ----------------------------------------------------------------

        /// <summary>
        /// 任务参数（JSON格式，按 TaskType 存储对应算法参数）。
        /// PatternMatch: {"TemplatePath": "...", "MinScore": 0.8, "MaxMatches": 1}
        /// BlobAnalysis: {"MinArea": 100, "MaxArea": 10000, "Circularity": 0.8}
        /// OCR: {"Languages": ["zh", "en"], "RegionOfInterest": {...}}
        /// </summary>
        public string Parameters { get; set; } = "{}";

        /// <summary>相机配置ID（指定使用哪台相机采集图像）</summary>
        public string? CameraId { get; set; }

        // ----------------------------------------------------------------
        // 执行结果（仅执行记录使用，模板此字段为null）
        // ----------------------------------------------------------------

        /// <summary>执行状态（null=模板，true=检测通过，false=检测不通过）</summary>
        public bool? IsPass { get; set; }

        /// <summary>匹配或置信度得分（0.0~1.0，null=模板或未执行）</summary>
        public double? Score { get; set; }

        /// <summary>实际执行耗时（毫秒，null=模板或未执行）</summary>
        public long? ElapsedMs { get; set; }

        /// <summary>原始采集图像路径（null=模板）</summary>
        public string? SourceImagePath { get; set; }

        /// <summary>标注结果图像路径（null=模板或未生成结果图）</summary>
        public string? ResultImagePath { get; set; }

        /// <summary>执行结果详情（JSON格式，包含算法输出的坐标、尺寸、字符串等）</summary>
        public string? ResultDetail { get; set; }

        /// <summary>执行时间（UTC，null=模板）</summary>
        public DateTime? ExecutedAt { get; set; }

        /// <summary>错误信息（null=成功或模板，非null=执行失败的异常描述）</summary>
        public string? ErrorMessage { get; set; }

        // ----------------------------------------------------------------
        // 关联关系
        // ----------------------------------------------------------------

        /// <summary>来源模板ID（执行记录引用的配置模板，null=本身就是模板）</summary>
        public Guid? TemplateId { get; set; }
    }
}
