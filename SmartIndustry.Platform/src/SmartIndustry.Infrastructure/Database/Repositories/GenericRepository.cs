// ============================================================
// 文件：GenericRepository.cs
// 层次：基础设施层 (Infrastructure Layer) — 泛型仓储实现
// 职责：
//   实现领域层定义的 IRepository<T> 接口，提供标准 CRUD 操作。
//   基于 EF Core DbContext，所有数据库操作通过 AppDbContext 执行。
// 设计思路：
//   泛型仓储（Generic Repository）避免为每个实体重复编写相同的 CRUD 代码。
//   通过 IQueryable<T> 延迟执行特性，FindAsync / GetPagedAsync 将过滤条件
//   推送到数据库执行（SQL WHERE），而非在内存中过滤，保证查询性能。
//   所有写操作（Add/Update/Delete）不立即提交事务，需调用 SaveChangesAsync，
//   支持在一个事务中执行多个操作后统一提交（工作单元模式）。
// 注意：
//   软删除过滤器由 AppDbContext.OnModelCreating 的全局 HasQueryFilter 处理，
//   仓储层无需手动过滤 IsDeleted，保持代码简洁。
//   要查询已删除的记录，需使用 dbContext.Set<T>().IgnoreQueryFilters() 绕过过滤器。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using Microsoft.EntityFrameworkCore;
using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Interfaces.Repositories;
using SmartIndustry.Infrastructure.Database.Context;
using System.Linq.Expressions;

namespace SmartIndustry.Infrastructure.Database.Repositories
{
    /// <summary>
    /// 泛型仓储实现，基于 EF Core AppDbContext。
    /// 实现 IRepository&lt;T&gt; 定义的所有标准持久化操作。
    /// </summary>
    /// <typeparam name="T">实体类型，必须继承 BaseEntity</typeparam>
    public class GenericRepository<T> : IRepository<T> where T : BaseEntity
    {
        // ----------------------------------------------------------------
        // 受保护字段：子类可以直接访问 DbContext 和 DbSet，
        // 方便专用仓储（如 AlarmRepository）添加自定义查询
        // ----------------------------------------------------------------

        /// <summary>EF Core 数据库上下文（工作单元，管理事务和变更跟踪）</summary>
        protected readonly AppDbContext _context;

        /// <summary>当前实体类型的 DbSet（提供 LINQ 查询入口）</summary>
        protected readonly DbSet<T> _dbSet;

        /// <summary>
        /// 构造函数：通过 DI 注入 AppDbContext。
        /// 在 DI 容器中以 Scoped 生命周期注册，保证每个业务操作使用同一个 DbContext 实例。
        /// </summary>
        /// <param name="context">注入的 EF Core 数据库上下文</param>
        public GenericRepository(AppDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _dbSet = context.Set<T>();
        }

        // ================================================================
        // 查询操作实现
        // ================================================================

        /// <summary>
        /// 按 Guid 主键查询单个实体。
        /// 全局软删除过滤器自动排除已删除记录，无需显式过滤。
        /// </summary>
        public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            // FindAsync 使用 EF Core 身份映射缓存（先查内存再查数据库），性能优于 FirstOrDefaultAsync
            // 注意：FindAsync 绕过全局查询过滤器，需要额外处理软删除
            // 使用 FirstOrDefaultAsync 确保软删除过滤器生效
            return await _dbSet
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        }

        /// <summary>
        /// 获取所有未删除的实体列表（不分页）。
        /// 警告：大数据量时请使用 GetPagedAsync 代替
        /// </summary>
        public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _dbSet
                .OrderByDescending(e => e.CreatedAt) // 默认按创建时间降序
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// 按 Lambda 表达式条件查询实体列表。
        /// predicate 会被 EF Core 翻译为 SQL WHERE 子句，在数据库层面过滤。
        /// </summary>
        public virtual async Task<IReadOnlyList<T>> FindAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            return await _dbSet
                .Where(predicate)
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// 分页查询，支持自定义过滤条件和排序字段。
        /// 实现流程：
        ///   1. 应用过滤条件（WHERE）
        ///   2. 统计总记录数（COUNT，在 SKIP 前执行）
        ///   3. 应用排序（ORDER BY）
        ///   4. 应用分页（SKIP + TAKE）
        ///   5. 返回包含元数据的 PagedResult
        /// </summary>
        public virtual async Task<PagedResult<T>> GetPagedAsync(
            int pageIndex,
            int pageSize,
            Expression<Func<T, bool>>? predicate = null,
            Expression<Func<T, object>>? orderBy = null,
            bool descending = true,
            CancellationToken cancellationToken = default)
        {
            // 参数校验（防止负数页索引或零/负页大小导致 SQL 错误）
            if (pageIndex < 0) throw new ArgumentOutOfRangeException(nameof(pageIndex), "页索引不能为负数");
            if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize), "页大小必须大于零");

            // 构建基础查询（IQueryable 延迟执行，尚未访问数据库）
            IQueryable<T> query = _dbSet;

            // 应用过滤条件
            if (predicate != null)
                query = query.Where(predicate);

            // 统计总数（在分页前执行，得到满足条件的全量记录数）
            var totalCount = await query.CountAsync(cancellationToken);

            // 应用排序（EF Core 要求 Skip/Take 前必须有 OrderBy）
            if (orderBy != null)
            {
                query = descending
                    ? query.OrderByDescending(orderBy)
                    : query.OrderBy(orderBy);
            }
            else
            {
                // 默认按创建时间降序（最新记录排在前面）
                query = query.OrderByDescending(e => e.CreatedAt);
            }

            // 应用分页（OFFSET + FETCH 或 LIMIT + OFFSET，视数据库方言而定）
            var items = await query
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<T>
            {
                Items = items,
                TotalCount = totalCount,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        /// <summary>
        /// 统计满足条件的记录数。
        /// predicate 为 null 时统计所有记录（仍排除已软删除的）。
        /// </summary>
        public virtual async Task<int> CountAsync(
            Expression<Func<T, bool>>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            return predicate == null
                ? await _dbSet.CountAsync(cancellationToken)
                : await _dbSet.CountAsync(predicate, cancellationToken);
        }

        /// <summary>
        /// 判断是否存在满足条件的记录（比 CountAsync > 0 更高效，数据库可短路返回）。
        /// </summary>
        public virtual async Task<bool> AnyAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            return await _dbSet.AnyAsync(predicate, cancellationToken);
        }

        // ================================================================
        // 写操作实现
        // ================================================================

        /// <summary>
        /// 新增单个实体（标记为 Added 状态，不立即写库）。
        /// 需在操作完成后调用 SaveChangesAsync 提交事务。
        /// </summary>
        public virtual async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            await _dbSet.AddAsync(entity, cancellationToken);
        }

        /// <summary>
        /// 批量新增（单次数据库往返，使用 EF Core AddRange）。
        /// </summary>
        public virtual async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            await _dbSet.AddRangeAsync(entities, cancellationToken);
        }

        /// <summary>
        /// 更新实体（标记为 Modified 状态）。
        /// EF Core 的变更跟踪通常自动检测修改，此方法用于处理脱离跟踪后重新附加的实体。
        /// </summary>
        public virtual void Update(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            // Attach 确保实体被跟踪，Entry.State = Modified 标记所有属性为已修改
            _dbSet.Update(entity);
        }

        /// <summary>
        /// 软删除实体（设置 IsDeleted = true，由 AppDbContext.SaveChanges 的 Deleted 拦截器处理）。
        /// 不物理删除数据库记录，保留审计数据。
        /// </summary>
        public virtual void Delete(T entity, string? deletedBy = null)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            // 调用实体的软删除方法（领域行为）
            entity.IsDeleted = true;
            entity.UpdatedBy = deletedBy ?? _context.CurrentUser ?? "System";
            entity.UpdatedAt = DateTime.UtcNow;

            // 标记为 Modified（AppDbContext 的 FillAuditFields 中的 Deleted 拦截也会处理）
            _context.Entry(entity).State = EntityState.Modified;
        }

        /// <summary>
        /// 按主键软删除实体（先查询再删除，找不到则静默忽略）。
        /// </summary>
        public virtual async Task DeleteByIdAsync(Guid id, string? deletedBy = null,
            CancellationToken cancellationToken = default)
        {
            var entity = await GetByIdAsync(id, cancellationToken);
            if (entity != null)
                Delete(entity, deletedBy);
        }

        // ================================================================
        // 工作单元：提交变更
        // ================================================================

        /// <summary>
        /// 提交所有挂起的变更到数据库。
        /// 内部调用 AppDbContext.SaveChangesAsync，触发审计字段填充和事务提交。
        /// </summary>
        /// <returns>受影响的数据库行数</returns>
        public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
