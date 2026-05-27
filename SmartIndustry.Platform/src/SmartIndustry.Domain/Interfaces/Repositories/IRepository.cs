// ============================================================
// 文件：IRepository.cs
// 层次：领域层 (Domain Layer) — 仓储接口
// 职责：定义泛型仓储的标准 CRUD 操作契约，实现持久化透明
// 设计思路：
//   仓储模式（Repository Pattern）将领域层与数据访问技术解耦。
//   领域层只依赖此接口，不感知 EF Core、SQLite 等具体技术。
//   Infrastructure 层提供具体实现，测试时可注入内存仓储实现。
//   PagedResult<T> 将分页元数据与数据捆绑，避免调用方多次往返查询。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Entities;
using System.Linq.Expressions;

namespace SmartIndustry.Domain.Interfaces.Repositories
{
    /// <summary>
    /// 分页结果包装类，封装分页数据和分页元信息
    /// </summary>
    /// <typeparam name="T">数据条目类型</typeparam>
    public class PagedResult<T>
    {
        /// <summary>当前页数据集合</summary>
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

        /// <summary>符合查询条件的总记录数（不受分页限制）</summary>
        public int TotalCount { get; init; }

        /// <summary>当前页索引（从0开始）</summary>
        public int PageIndex { get; init; }

        /// <summary>每页记录数</summary>
        public int PageSize { get; init; }

        /// <summary>总页数（计算属性，TotalCount / PageSize 向上取整）</summary>
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

        /// <summary>是否有上一页</summary>
        public bool HasPreviousPage => PageIndex > 0;

        /// <summary>是否有下一页</summary>
        public bool HasNextPage => PageIndex < TotalPages - 1;
    }

    /// <summary>
    /// 泛型仓储接口，定义所有实体共用的持久化操作。
    /// Infrastructure 层的 GenericRepository&lt;T&gt; 实现此接口。
    /// </summary>
    /// <typeparam name="T">实体类型，必须继承 BaseEntity</typeparam>
    public interface IRepository<T> where T : BaseEntity
    {
        // ----------------------------------------------------------------
        // 查询操作
        // ----------------------------------------------------------------

        /// <summary>
        /// 按主键查询单个实体（不包含已软删除的记录）。
        /// </summary>
        /// <param name="id">实体 Guid 主键</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>找到的实体，或 null（主键不存在时）</returns>
        Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取所有实体（不包含已软删除，不分页，谨慎在大数据量场景使用）。
        /// </summary>
        Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 按条件表达式查询实体列表。
        /// </summary>
        /// <param name="predicate">过滤条件（LINQ 表达式，EF Core 自动翻译为 SQL WHERE）</param>
        Task<IReadOnlyList<T>> FindAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 按条件分页查询，支持自定义排序。
        /// </summary>
        /// <param name="pageIndex">页索引（从0开始）</param>
        /// <param name="pageSize">每页记录数（建议不超过500）</param>
        /// <param name="predicate">过滤条件（null=不过滤）</param>
        /// <param name="orderBy">排序表达式（null=按 CreatedAt 降序）</param>
        /// <param name="descending">排序方向（true=降序，false=升序）</param>
        Task<PagedResult<T>> GetPagedAsync(
            int pageIndex,
            int pageSize,
            Expression<Func<T, bool>>? predicate = null,
            Expression<Func<T, object>>? orderBy = null,
            bool descending = true,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 统计满足条件的记录数。
        /// </summary>
        Task<int> CountAsync(
            Expression<Func<T, bool>>? predicate = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 判断是否存在满足条件的记录。
        /// </summary>
        Task<bool> AnyAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 写操作
        // ----------------------------------------------------------------

        /// <summary>
        /// 新增单个实体（不会立即写库，需调用 SaveChangesAsync 提交）。
        /// </summary>
        Task AddAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量新增实体（单次数据库往返，比循环调用 AddAsync 更高效）。
        /// </summary>
        Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新实体（标记为已修改状态，不会立即写库）。
        /// </summary>
        void Update(T entity);

        /// <summary>
        /// 软删除实体（设置 IsDeleted=true，不物理删除数据库记录）。
        /// </summary>
        /// <param name="entity">要删除的实体</param>
        /// <param name="deletedBy">操作人用户名</param>
        void Delete(T entity, string? deletedBy = null);

        /// <summary>
        /// 按主键软删除实体。
        /// </summary>
        Task DeleteByIdAsync(Guid id, string? deletedBy = null, CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 工作单元：提交变更
        // ----------------------------------------------------------------

        /// <summary>
        /// 提交所有挂起的变更到数据库（BEGIN TRANSACTION + COMMIT）。
        /// </summary>
        /// <returns>受影响的数据库行数</returns>
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
