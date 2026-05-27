// ============================================================
// 文件：Recipe.cs
// 层次：领域层 (Domain Layer) — 实体
// 职责：表示生产配方，包含工艺参数集合和版本管理信息
// 设计思路：
//   配方采用版本化管理（RecipeName+Version 联合唯一键），
//   生产使用时记录配方版本号，便于质量回溯。
//   Parameters 字段以 JSON 格式存储，支持不同工艺类型的灵活参数结构。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Entities
{
    /// <summary>
    /// 生产配方实体，对应数据库表 Recipes。
    /// 存储工艺参数集合，支持版本化管理和生命周期状态控制。
    /// </summary>
    public class Recipe : BaseEntity
    {
        // ----------------------------------------------------------------
        // 配方标识
        // ----------------------------------------------------------------

        /// <summary>
        /// 配方名称（同一配方的不同版本共享相同 RecipeName）。
        /// 与 Version 联合构成业务唯一键，如："晶圆传输配方A" + Version=3
        /// </summary>
        public string RecipeName { get; set; } = string.Empty;

        /// <summary>配方版本号（从1开始递增，同名配方每次修改后版本号+1）
        /// 注：使用 new 关键字有意遮蔽 BaseEntity.Version（byte[]乐观并发字段），
        /// 因为配方有自己的业务版本号语义（int）</summary>
        public new int Version { get; set; } = 1;

        /// <summary>配方描述（记录本版本的修改内容和适用工艺条件）</summary>
        public string Description { get; set; } = string.Empty;

        // ----------------------------------------------------------------
        // 配方工艺参数
        // ----------------------------------------------------------------

        /// <summary>
        /// 工艺参数 JSON 字符串（灵活结构，按产品类型存储不同参数集合）。
        /// 建议格式：{"MotionParams": {...}, "VisionParams": {...}, "ProcessParams": {...}}
        /// Application 层负责序列化/反序列化，领域层只存储字符串
        /// </summary>
        public string Parameters { get; set; } = "{}";

        // ----------------------------------------------------------------
        // 配方生命周期状态
        // ----------------------------------------------------------------

        /// <summary>配方状态（Draft=草稿，Active=激活生产，Archived=归档停用）</summary>
        public RecipeState State { get; set; } = RecipeState.Draft;

        // ----------------------------------------------------------------
        // 关联关系
        // ----------------------------------------------------------------

        /// <summary>导航属性：此配方关联的轴配置列表（一对多）</summary>
        public ICollection<AxisConfig> AxisConfigs { get; set; } = new List<AxisConfig>();

        // ----------------------------------------------------------------
        // 领域行为
        // ----------------------------------------------------------------

        /// <summary>
        /// 激活配方（仅草稿状态可激活，激活后进入生产可用状态）
        /// </summary>
        /// <exception cref="InvalidOperationException">非草稿状态时抛出</exception>
        public void Activate(string operatedBy)
        {
            if (State != RecipeState.Draft)
                throw new InvalidOperationException($"只有草稿状态的配方才能激活，当前状态：{State}");

            State = RecipeState.Active;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = operatedBy;
        }

        /// <summary>归档配方（停用后只读，不可重新激活）</summary>
        public void Archive(string operatedBy)
        {
            State = RecipeState.Archived;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = operatedBy;
        }
    }
}
