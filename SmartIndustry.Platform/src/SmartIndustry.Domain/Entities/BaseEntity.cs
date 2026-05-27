// ============================================================
// 文件：BaseEntity.cs
// 层次：领域层 (Domain Layer) — 实体基类
// 职责：
//   1. 提供所有领域实体的公共属性（标识、审计字段、软删除、版本）
//   2. 维护领域事件集合，支持 DDD 事件驱动架构
//   3. 实现乐观并发控制机制，防止并发写入数据覆盖
// 设计思路：
//   采用"富领域模型"风格，实体自身持有领域事件列表，
//   由 Application 层在持久化后统一分发，保证事件与数据一致性。
//   Version 字段由 ORM（EF Core）自动管理，基类不暴露 setter。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Events;

namespace SmartIndustry.Domain.Entities
{
    /// <summary>
    /// 领域实体抽象基类
    /// 所有持久化领域对象必须继承此类，获得标准审计能力和事件发布能力。
    /// </summary>
    public abstract class BaseEntity
    {
        // ----------------------------------------------------------------
        // 私有字段：领域事件列表（未分发前暂存于实体内部）
        // ----------------------------------------------------------------

        /// <summary>
        /// 领域事件暂存列表。
        /// 使用私有列表防止外部直接操控，只允许通过 AddDomainEvent 方法追加。
        /// </summary>
        private readonly List<DomainEvent> _domainEvents = new();

        // ----------------------------------------------------------------
        // 核心标识属性
        // ----------------------------------------------------------------

        /// <summary>
        /// 实体唯一标识符（GUID）。
        /// 使用 GUID 而非自增整型，避免分布式环境下 ID 冲突，
        /// 并支持客户端在持久化前生成 ID（用于事件溯源场景）。
        /// </summary>
        public Guid Id { get; protected set; } = Guid.NewGuid();

        // ----------------------------------------------------------------
        // 审计字段：记录实体创建和最后修改的时间与操作人
        // ----------------------------------------------------------------

        /// <summary>
        /// 实体创建时间（UTC）。
        /// 存储为 UTC 时间，显示层按需转换为本地时区。
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 实体最后更新时间（UTC）。
        /// 每次 SaveChanges 前由 Application 层或 DbContext 自动更新。
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 创建操作人用户名。
        /// 可能为 null（系统自动创建的记录，如启动时自动初始化的配置）。
        /// </summary>
        public string? CreatedBy { get; set; }

        /// <summary>
        /// 最后修改操作人用户名。
        /// </summary>
        public string? UpdatedBy { get; set; }

        // ----------------------------------------------------------------
        // 软删除：逻辑删除，不从数据库物理移除记录
        // ----------------------------------------------------------------

        /// <summary>
        /// 软删除标志。
        /// true 表示记录已被逻辑删除，查询时应通过全局过滤器排除。
        /// 物理删除需要管理员级别的显式操作，不走此字段。
        /// </summary>
        public bool IsDeleted { get; set; } = false;

        // ----------------------------------------------------------------
        // 乐观并发控制：防止多用户同时修改同一条记录时数据覆盖
        // ----------------------------------------------------------------

        /// <summary>
        /// 实体版本号（乐观并发令牌）。
        /// EF Core 将此字段映射为数据库 rowversion/timestamp 列，
        /// 更新时自动检测冲突并抛出 DbUpdateConcurrencyException。
        /// 注：EF Core 会管理此字段，不需要业务代码手动赋值。
        /// </summary>
        public byte[] Version { get; set; } = Array.Empty<byte>();

        // ----------------------------------------------------------------
        // 领域事件支持
        // ----------------------------------------------------------------

        /// <summary>
        /// 只读的领域事件集合（外部只能读取，不能直接修改）。
        /// Application 层在事务提交后遍历此集合并分发事件。
        /// </summary>
        public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

        /// <summary>
        /// 向实体内部追加一个领域事件。
        /// </summary>
        /// <param name="domainEvent">要追加的领域事件实例</param>
        protected void AddDomainEvent(DomainEvent domainEvent)
        {
            _domainEvents.Add(domainEvent);
        }

        /// <summary>
        /// 清空所有已暂存的领域事件（由 Application 层在分发完成后调用）。
        /// </summary>
        public void ClearDomainEvents()
        {
            _domainEvents.Clear();
        }

        // ----------------------------------------------------------------
        // 逻辑删除辅助方法
        // ----------------------------------------------------------------

        /// <summary>
        /// 执行软删除：设置 IsDeleted 标志并记录操作人。
        /// </summary>
        /// <param name="operatorName">执行删除操作的用户名</param>
        public virtual void SoftDelete(string operatorName)
        {
            // 防止重复删除
            if (IsDeleted) return;

            IsDeleted = true;
            UpdatedBy = operatorName;
            UpdatedAt = DateTime.UtcNow;
        }

        // ----------------------------------------------------------------
        // 值相等性：领域实体按 Id 比较，而非引用比较
        // ----------------------------------------------------------------

        /// <summary>
        /// 判断两个实体是否代表同一个领域对象（按 Id 比较）。
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is not BaseEntity other) return false;
            if (ReferenceEquals(this, other)) return true;
            if (GetType() != other.GetType()) return false;
            // 新实体（Id 全零）不认为相等，避免未持久化对象混淆
            if (Id == Guid.Empty || other.Id == Guid.Empty) return false;
            return Id == other.Id;
        }

        /// <summary>
        /// 返回基于 Id 的哈希码，保证相同 Id 的实体哈希一致。
        /// </summary>
        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        /// <summary>
        /// 返回实体的调试友好字符串，包含类型名和 Id。
        /// </summary>
        public override string ToString()
        {
            return $"{GetType().Name}[Id={Id}]";
        }

        // ----------------------------------------------------------------
        // 相等运算符重载
        // ----------------------------------------------------------------

        public static bool operator ==(BaseEntity? left, BaseEntity? right)
        {
            if (left is null && right is null) return true;
            if (left is null || right is null) return false;
            return left.Equals(right);
        }

        public static bool operator !=(BaseEntity? left, BaseEntity? right)
        {
            return !(left == right);
        }
    }
}
