// ============================================================
// 文件：IDatabaseService.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义数据库访问服务接口，实现数据持久化的领域层契约
// 设计思路：
//   IDatabaseService 是领域层对数据库的依赖声明，采用"仓储模式的简化版"。
//   与标准仓储模式（IRepository<T>）的区别：
//     - 标准仓储：每个聚合根一个仓储接口（IAlarmRepository、IAxisRepository 等）
//     - 此接口：通用数据库服务，适合轻量级 CRUD 和 SQL 查询场景
//   两种模式可共存：复杂聚合根使用专用仓储，简单 CRUD 使用此接口。
//   事务支持：BeginTransactionAsync 返回 IDbTransaction，
//   调用方 using 块内的操作在同一事务中执行。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 通用数据库访问服务接口。
    /// 提供类型安全的 CRUD 操作和原生 SQL 执行能力。
    /// </summary>
    public interface IDatabaseService : IAsyncDisposable
    {
        // ----------------------------------------------------------------
        // 查询操作
        // ----------------------------------------------------------------

        /// <summary>
        /// 异步查询并返回结果集（映射到强类型列表）。
        /// </summary>
        /// <typeparam name="T">结果实体类型</typeparam>
        /// <param name="sql">SQL 查询语句（支持参数化查询，防止 SQL 注入）</param>
        /// <param name="parameters">SQL 参数对象（匿名对象或字典形式）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>查询结果列表（无结果时返回空列表，而非 null）</returns>
        Task<IReadOnlyList<T>> QueryAsync<T>(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 查询单个实体（无结果时返回 null，多条结果时抛出异常）。
        /// </summary>
        Task<T?> QueryFirstOrDefaultAsync<T>(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 执行操作（INSERT/UPDATE/DELETE）
        // ----------------------------------------------------------------

        /// <summary>
        /// 执行非查询 SQL（INSERT/UPDATE/DELETE/DDL）。
        /// </summary>
        /// <returns>受影响的行数</returns>
        Task<int> ExecuteAsync(
            string sql,
            object? parameters = null,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 实体级 CRUD 操作（ORM 自动生成 SQL）
        // ----------------------------------------------------------------

        /// <summary>
        /// 插入单个实体到对应数据表。
        /// </summary>
        /// <typeparam name="T">实体类型（需有对应数据表映射）</typeparam>
        /// <param name="entity">要插入的实体实例</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task InsertAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// 更新实体（按主键定位，更新所有可写字段）。
        /// 乐观并发：若 Version 字段不匹配则抛出并发异常。
        /// </summary>
        Task UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// 软删除实体（设置 IsDeleted=true，不物理删除记录）。
        /// </summary>
        Task DeleteAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class;

        // ----------------------------------------------------------------
        // 批量操作（高性能，适合大数据量写入）
        // ----------------------------------------------------------------

        /// <summary>
        /// 批量插入实体集合（使用 BULK INSERT 或等价高性能写入方式）。
        /// 典型场景：报警历史、运动轨迹、视觉结果的批量记录。
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="entities">要批量插入的实体集合</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task BulkInsertAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default)
            where T : class;

        // ----------------------------------------------------------------
        // 事务支持
        // ----------------------------------------------------------------

        /// <summary>
        /// 开始数据库事务，返回事务作用域对象。
        /// 在 using 块内所有数据库操作在同一事务中执行。
        /// 使用示例：
        ///   await using var tx = await db.BeginTransactionAsync();
        ///   await db.InsertAsync(entity1);
        ///   await db.UpdateAsync(entity2);
        ///   await tx.CommitAsync();
        /// </summary>
        Task<IDbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 数据库事务作用域接口。
    /// 由 IDatabaseService.BeginTransactionAsync 返回，支持提交和回滚。
    /// </summary>
    public interface IDbTransaction : IAsyncDisposable
    {
        /// <summary>提交事务（所有操作永久生效）</summary>
        Task CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>回滚事务（所有操作撤销，回到事务开始前的状态）</summary>
        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}
