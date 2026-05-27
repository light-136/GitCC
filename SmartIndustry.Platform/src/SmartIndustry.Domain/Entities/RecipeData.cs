// ============================================================
// 文件：RecipeData.cs
// 层次：领域层 (Domain Layer) — 实体
// 职责：工艺配方实体，管理生产参数的版本化存储和生命周期
// 设计思路：
//   配方管理是工业自动化中防错（Poka-Yoke）的核心机制。
//   此实体实现了：
//     1. 版本链追踪：ParentVersionId 形成配方变更历史的有向链表
//     2. 状态机控制：Draft -> Active -> Archived，防止草稿版本用于生产
//     3. 参数字典：使用 Dictionary<string, object> 存储任意类型的工艺参数，
//        实现配方格式的通用性（不同工序的参数结构不同）
//     4. 激活排他性：系统同一时间只能有一个 Active 状态的同名配方
//        （由 IRecipeService 业务层保证，而非领域层 DB 约束）
//   参数序列化：Dictionary<string, object> 由 Infrastructure 层序列化为 JSON 存储。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.Events;

namespace SmartIndustry.Domain.Entities
{
    /// <summary>
    /// 工艺配方实体。
    /// 存储生产过程中的工艺参数集合，支持版本管理和生命周期控制。
    /// </summary>
    public class RecipeData : BaseEntity
    {
        // ----------------------------------------------------------------
        // 配方基本信息
        // ----------------------------------------------------------------

        /// <summary>
        /// 配方名称（业务标识符，同名配方构成一个版本序列）。
        /// 示例："产品A-贴片程序"、"螺丝拧紧-M3-标准"。
        /// 同一名称可存在多个版本，但只允许一个处于 Active 状态。
        /// </summary>
        public string Name { get; private set; } = string.Empty;

        /// <summary>
        /// 配方描述（说明此配方适用的产品型号、工序和注意事项）。
        /// </summary>
        public string Description { get; private set; } = string.Empty;

        /// <summary>
        /// 配方当前状态（Draft/Active/Archived）。
        /// </summary>
        public RecipeState State { get; private set; } = RecipeState.Draft;

        // ----------------------------------------------------------------
        // 工艺参数存储
        // ----------------------------------------------------------------

        /// <summary>
        /// 工艺参数字典（键为参数名，值为参数值）。
        /// 支持混合类型：数字、字符串、布尔、嵌套字典等。
        /// 示例：
        ///   { "SpeedX": 500.0, "SpeedY": 300.0, "VisionThreshold": 0.85,
        ///     "StationCount": 4, "EnableVision": true }
        /// 由 Infrastructure 层序列化为 JSON 列存储。
        /// </summary>
        public Dictionary<string, object> Parameters { get; private set; } = new();

        // ----------------------------------------------------------------
        // 版本管理
        // ----------------------------------------------------------------

        /// <summary>
        /// 配方版本号（同名配方按创建顺序递增的整数版本号，从 1 开始）。
        /// 升版时由 Application 层递增后传入工厂方法。
        /// </summary>
        public int VersionNumber { get; private set; } = 1;

        /// <summary>
        /// 父版本的配方 ID（指向此版本的来源配方，形成版本链）。
        /// null 表示此版本是该配方的初始版本（无父版本）。
        /// 用于 UI 显示版本历史树和 Diff 对比功能。
        /// </summary>
        public Guid? ParentVersionId { get; private set; }

        // ----------------------------------------------------------------
        // 激活审计
        // ----------------------------------------------------------------

        /// <summary>
        /// 配方被激活的时间（UTC，null 表示从未激活或已归档）。
        /// </summary>
        public DateTime? ActivatedAt { get; private set; }

        /// <summary>
        /// 激活操作人用户名（null 表示从未激活）。
        /// </summary>
        public string? ActivatedBy { get; private set; }

        // ----------------------------------------------------------------
        // 私有构造（EF Core 反射用）
        // ----------------------------------------------------------------
        private RecipeData() { }

        // ----------------------------------------------------------------
        // 工厂方法
        // ----------------------------------------------------------------

        /// <summary>
        /// 创建新配方（首版本），状态默认为 Draft。
        /// </summary>
        /// <param name="name">配方名称</param>
        /// <param name="description">配方描述</param>
        /// <param name="parameters">初始工艺参数字典</param>
        /// <param name="createdBy">创建人</param>
        public static RecipeData CreateNew(
            string name,
            string description,
            Dictionary<string, object>? parameters = null,
            string? createdBy = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("配方名称不能为空。", nameof(name));

            var recipe = new RecipeData
            {
                Name = name,
                Description = description,
                State = RecipeState.Draft,
                Parameters = parameters ?? new Dictionary<string, object>(),
                VersionNumber = 1,
                ParentVersionId = null,
                CreatedBy = createdBy,
                UpdatedBy = createdBy
            };

            // 发布配方变更事件（首次创建，oldVersion=null）
            recipe.AddDomainEvent(new RecipeChangedEvent(name, null, 1));

            return recipe;
        }

        /// <summary>
        /// 从现有配方创建新版本（升版工厂方法）。
        /// 新版本继承上一版本的参数，状态重置为 Draft。
        /// </summary>
        /// <param name="parent">作为基础的上一版本配方</param>
        /// <param name="newParameters">更新后的工艺参数（null 表示继承父版本参数）</param>
        /// <param name="createdBy">创建人</param>
        public static RecipeData CreateNewVersion(
            RecipeData parent,
            Dictionary<string, object>? newParameters = null,
            string? createdBy = null)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            int nextVersion = parent.VersionNumber + 1;

            var recipe = new RecipeData
            {
                Name = parent.Name,
                Description = parent.Description,
                State = RecipeState.Draft,
                Parameters = newParameters ?? new Dictionary<string, object>(parent.Parameters),
                VersionNumber = nextVersion,
                ParentVersionId = parent.Id,
                CreatedBy = createdBy,
                UpdatedBy = createdBy
            };

            recipe.AddDomainEvent(new RecipeChangedEvent(parent.Name, parent.VersionNumber, nextVersion));

            return recipe;
        }

        // ----------------------------------------------------------------
        // 领域行为方法
        // ----------------------------------------------------------------

        /// <summary>
        /// 激活此配方（从 Draft 状态变为 Active）。
        /// 注意：Application 层在调用此方法前，需要先将同名的其他 Active 配方归档。
        /// </summary>
        /// <param name="activatedBy">激活操作人</param>
        /// <exception cref="InvalidOperationException">非 Draft 状态时抛出</exception>
        public void Activate(string activatedBy)
        {
            if (State != RecipeState.Draft)
                throw new InvalidOperationException($"配方 [{Name} v{VersionNumber}] 当前状态为 {State}，只有 Draft 状态的配方才能被激活。");

            State = RecipeState.Active;
            ActivatedAt = DateTime.UtcNow;
            ActivatedBy = activatedBy;
            UpdatedBy = activatedBy;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 归档此配方（Active 或 Draft -> Archived，不可逆）。
        /// 归档后配方进入只读历史状态，不可再激活或修改。
        /// </summary>
        /// <param name="archivedBy">归档操作人</param>
        public void Archive(string archivedBy)
        {
            if (State == RecipeState.Archived)
                return; // 已归档，幂等操作

            State = RecipeState.Archived;
            UpdatedBy = archivedBy;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 更新配方参数（仅允许 Draft 状态的配方修改参数）。
        /// </summary>
        /// <param name="parameters">新的工艺参数字典</param>
        /// <param name="operatedBy">操作人</param>
        public void UpdateParameters(Dictionary<string, object> parameters, string operatedBy)
        {
            if (State != RecipeState.Draft)
                throw new InvalidOperationException($"配方 [{Name} v{VersionNumber}] 状态为 {State}，只有 Draft 状态的配方可以修改参数。");

            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            UpdatedBy = operatedBy;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 设置或更新单个参数值（仅允许 Draft 状态）。
        /// </summary>
        /// <param name="key">参数键名</param>
        /// <param name="value">参数值</param>
        public void SetParameter(string key, object value, string operatedBy)
        {
            if (State != RecipeState.Draft)
                throw new InvalidOperationException($"配方 [{Name} v{VersionNumber}] 非草稿状态，不可修改参数。");

            Parameters[key] = value;
            UpdatedBy = operatedBy;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 获取参数值（带类型转换，参数不存在时返回默认值）。
        /// </summary>
        /// <typeparam name="T">期望的参数值类型</typeparam>
        /// <param name="key">参数键名</param>
        /// <param name="defaultValue">参数不存在时的默认值</param>
        public T? GetParameter<T>(string key, T? defaultValue = default)
        {
            if (Parameters.TryGetValue(key, out var value) && value is T typedValue)
                return typedValue;
            return defaultValue;
        }

        /// <summary>
        /// 判断配方是否可用于生产（只有 Active 状态才可使用）。
        /// </summary>
        public bool IsProductionReady => State == RecipeState.Active;
    }
}
