// ============================================================
// 文件：IRecipeService.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义工艺配方管理服务接口（CRUD + 版本控制 + 导出导入）
// 设计思路：
//   配方管理是工业自动化防错（Poka-Yoke）的核心机制。
//   此接口覆盖配方的完整生命周期：
//     创建（Draft）-> 激活（Active）-> 归档（Archived）
//   ActivateRecipe 是关键操作，需要：
//     1. 将目标配方从 Draft 变为 Active
//     2. 将同名的当前 Active 配方归档（一个产品只有一个 Active 配方）
//     3. 发布 RecipeChangedEvent 通知运动/视觉模块加载新参数
//   导出导入（JSON 格式）支持：
//     - 在不同设备之间迁移配方
//     - 作为配方备份手段
//     - 从 ERP/MES 系统导入工艺参数
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Entities;

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 工艺配方管理服务接口。
    /// 负责配方的全生命周期管理，包括版本控制、激活切换和跨设备迁移。
    /// </summary>
    public interface IRecipeService
    {
        // ----------------------------------------------------------------
        // 配方查询
        // ----------------------------------------------------------------

        /// <summary>
        /// 获取所有配方（包含所有版本，按名称和版本号排序）。
        /// </summary>
        Task<IReadOnlyList<RecipeData>> GetAllRecipes(CancellationToken cancellationToken = default);

        /// <summary>
        /// 按 ID 获取单个配方版本。
        /// </summary>
        /// <returns>配方实体，不存在时返回 null</returns>
        Task<RecipeData?> GetRecipe(Guid recipeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取指定名称的所有历史版本（版本链）。
        /// </summary>
        Task<IReadOnlyList<RecipeData>> GetRecipeVersions(
            string recipeName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前激活的配方（指定名称的 Active 状态版本）。
        /// </summary>
        /// <returns>激活中的配方，不存在时返回 null</returns>
        Task<RecipeData?> GetActiveRecipe(string recipeName, CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 配方创建与修改
        // ----------------------------------------------------------------

        /// <summary>
        /// 创建新配方（首版本，状态为 Draft）。
        /// </summary>
        Task<RecipeData> CreateRecipe(
            string name,
            string description,
            Dictionary<string, object>? parameters,
            string createdBy,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新配方参数（仅允许 Draft 状态的配方）。
        /// </summary>
        Task UpdateRecipe(
            Guid recipeId,
            Dictionary<string, object> parameters,
            string operatedBy,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 基于现有版本创建新版本（升版，状态为 Draft）。
        /// </summary>
        Task<RecipeData> CreateNewVersion(
            Guid parentRecipeId,
            Dictionary<string, object>? newParameters,
            string createdBy,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 配方生命周期控制
        // ----------------------------------------------------------------

        /// <summary>
        /// 删除配方（软删除，仅允许删除 Draft 状态的配方）。
        /// Active/Archived 状态的配方不可删除，需先归档再删除。
        /// </summary>
        Task DeleteRecipe(Guid recipeId, string operatedBy, CancellationToken cancellationToken = default);

        /// <summary>
        /// 激活配方（Draft -> Active，自动将同名旧 Active 配方归档）。
        /// 发布 RecipeChangedEvent 通知相关模块加载新参数。
        /// </summary>
        Task ActivateRecipe(Guid recipeId, string activatedBy, CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 导出导入（跨设备迁移 / 备份恢复）
        // ----------------------------------------------------------------

        /// <summary>
        /// 将配方导出为 JSON 字符串（包含完整参数和版本信息）。
        /// </summary>
        Task<string> ExportRecipe(Guid recipeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 从 JSON 字符串导入配方（创建新的 Draft 版本，不激活）。
        /// </summary>
        /// <param name="jsonContent">从 ExportRecipe 导出的 JSON 字符串</param>
        /// <param name="importedBy">导入操作人</param>
        Task<RecipeData> ImportRecipe(
            string jsonContent,
            string importedBy,
            CancellationToken cancellationToken = default);
    }
}
