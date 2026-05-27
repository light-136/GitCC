// ============================================================
// 文件：AxisConfig.cs
// 层次：领域层 (Domain Layer) — 实体
// 职责：表示运动轴的配置信息，包括硬件参数和运动规划默认参数
// 设计思路：
//   轴配置是持久化的设备参数，修改后需要重新下载到运动控制卡。
//   SoftLimit（软限位）作为值字段内嵌，避免过度拆分表。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Entities
{
    /// <summary>
    /// 运动轴配置实体。
    /// 对应数据库表 AxisConfigs，存储运动控制卡轴参数。
    /// </summary>
    public class AxisConfig : BaseEntity
    {
        // ----------------------------------------------------------------
        // 基本标识属性
        // ----------------------------------------------------------------

        /// <summary>轴编号（运动控制卡的物理轴索引，从0开始）</summary>
        public int AxisIndex { get; set; }

        /// <summary>轴显示名称（如"X轴"、"升降轴"），用于UI展示和日志描述</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>轴功能描述（如"负责晶圆水平传输的X方向轴"）</summary>
        public string Description { get; set; } = string.Empty;

        // ----------------------------------------------------------------
        // 当前运行状态（运行时更新，非持久化参数）
        // ----------------------------------------------------------------

        /// <summary>当前轴运行状态（运行时由硬件驱动层更新）</summary>
        public AxisState CurrentState { get; set; } = AxisState.Disabled;

        /// <summary>当前实际位置（单位：脉冲或mm，取决于 PulsePerMm 配置）</summary>
        public double CurrentPosition { get; set; }

        // ----------------------------------------------------------------
        // 硬件参数
        // ----------------------------------------------------------------

        /// <summary>每毫米对应的脉冲数（电子齿轮比 × 编码器分辨率），用于位置单位换算</summary>
        public double PulsePerMm { get; set; } = 1000.0;

        /// <summary>软限位最小值（脉冲/mm，运动指令超出此范围将被拒绝）</summary>
        public double SoftLimitMin { get; set; } = -999999.0;

        /// <summary>软限位最大值（脉冲/mm）</summary>
        public double SoftLimitMax { get; set; } = 999999.0;

        /// <summary>是否启用软限位检查（调试时可临时关闭，生产时必须启用）</summary>
        public bool SoftLimitEnabled { get; set; } = true;

        // ----------------------------------------------------------------
        // 默认运动参数
        // ----------------------------------------------------------------

        /// <summary>默认最大速度（mm/s）</summary>
        public double DefaultVelocity { get; set; } = 100.0;

        /// <summary>默认加速度（mm/s²）</summary>
        public double DefaultAcceleration { get; set; } = 1000.0;

        /// <summary>默认减速度（mm/s²）</summary>
        public double DefaultDeceleration { get; set; } = 1000.0;

        /// <summary>点动速度（mm/s），通常远低于定位速度，确保手动操作安全</summary>
        public double JogVelocity { get; set; } = 10.0;

        /// <summary>回零速度（mm/s），接近原点开关后切换为低速</summary>
        public double HomeVelocity { get; set; } = 50.0;

        // ----------------------------------------------------------------
        // 回零配置
        // ----------------------------------------------------------------

        /// <summary>回零方向（1=正方向，-1=负方向）</summary>
        public int HomeDirection { get; set; } = -1;

        /// <summary>回零后偏移量（mm），抬离限位开关后的停止位置</summary>
        public double HomeOffset { get; set; } = 2.0;

        // ----------------------------------------------------------------
        // 关联关系（导航属性）
        // ----------------------------------------------------------------

        /// <summary>所属配方ID（可为null，表示通用配置）</summary>
        public Guid? RecipeId { get; set; }

        /// <summary>导航属性：所属配方实体</summary>
        public Recipe? Recipe { get; set; }
    }
}
