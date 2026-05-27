// ============================================================
// 文件：Specification.cs
// 层次：领域层 (Domain Layer) — 规约模式
// 职责：实现规约（Specification）模式，将复杂的业务过滤条件封装为可组合的对象
// 设计思路：
//   规约模式（Specification Pattern）解决以下问题：
//     1. 将业务过滤逻辑从仓储方法中分离（避免仓储爆炸：GetByXxx 方法泛滥）
//     2. 使业务规则可复用、可组合、可测试
//     3. 支持 LINQ 表达式树，使规约可被 ORM（EF Core）翻译为 SQL
//   设计层次：
//     - ISpecification<T>：顶层接口，定义规约契约
//     - Specification<T>：抽象基类，持有 Expression 并实现 And/Or/Not 组合
//     - 具体规约类：继承 Specification<T>，封装单一业务过滤条件
//   组合示例：
//     var spec = new ActiveAlarmSpec()
//         .And(new AlarmLevelSpec(AlarmLevel.Critical))
//         .Or(new AlarmCategorySpec(AlarmCategory.Safety));
//   然后可在查询中使用：
//     alarms.Where(spec.IsSatisfiedBy)           // 内存过滤
//     dbContext.Alarms.Where(spec.ToExpression()) // EF Core SQL 翻译
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using System.Linq.Expressions;
using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Specifications
{
    // ====================================================================
    // 规约接口
    // ====================================================================

    /// <summary>
    /// 规约接口（顶层契约）。
    /// 定义规约的核心能力：判断一个对象是否满足规约条件。
    /// </summary>
    /// <typeparam name="T">规约适用的领域类型</typeparam>
    public interface ISpecification<T>
    {
        /// <summary>
        /// 判断对象是否满足规约（内存中判断，用于集合过滤）。
        /// </summary>
        bool IsSatisfiedBy(T entity);

        /// <summary>
        /// 将规约转换为 LINQ 表达式树（用于 EF Core 生成 SQL WHERE 子句）。
        /// </summary>
        Expression<Func<T, bool>> ToExpression();
    }

    // ====================================================================
    // 规约抽象基类（提供默认实现和组合操作符）
    // ====================================================================

    /// <summary>
    /// 规约抽象基类。
    /// 子类只需提供 Expression 表达式，自动获得 IsSatisfiedBy 和组合方法。
    /// </summary>
    /// <typeparam name="T">规约适用的领域类型</typeparam>
    public abstract class Specification<T> : ISpecification<T>
    {
        // ----------------------------------------------------------------
        // 核心方法（子类必须实现）
        // ----------------------------------------------------------------

        /// <summary>
        /// 返回描述规约条件的 LINQ 表达式树。
        /// 子类实现此方法来定义具体的过滤条件。
        /// </summary>
        public abstract Expression<Func<T, bool>> ToExpression();

        // ----------------------------------------------------------------
        // 默认实现（基于 Expression 自动编译，无需子类重写）
        // ----------------------------------------------------------------

        /// <summary>
        /// 判断实体是否满足规约（将 Expression 编译后执行）。
        /// 注意：频繁调用时建议缓存编译结果，避免重复 JIT 编译开销。
        /// </summary>
        public bool IsSatisfiedBy(T entity)
        {
            // Compile() 将表达式树编译为委托（有 JIT 编译开销，内存过滤时调用）
            var predicate = ToExpression().Compile();
            return predicate(entity);
        }

        // ----------------------------------------------------------------
        // 规约组合运算符（And / Or / Not）
        // ----------------------------------------------------------------

        /// <summary>
        /// 与组合（AND）：当前规约 AND 另一个规约都满足时返回 true。
        /// </summary>
        /// <param name="other">要与当前规约组合的另一规约</param>
        public Specification<T> And(Specification<T> other)
            => new AndSpecification<T>(this, other);

        /// <summary>
        /// 或组合（OR）：当前规约 OR 另一个规约任一满足时返回 true。
        /// </summary>
        public Specification<T> Or(Specification<T> other)
            => new OrSpecification<T>(this, other);

        /// <summary>
        /// 非组合（NOT）：对当前规约取反。
        /// </summary>
        public Specification<T> Not()
            => new NotSpecification<T>(this);

        // ----------------------------------------------------------------
        // 运算符重载（语法糖，允许使用 & | ! 操作符）
        // ----------------------------------------------------------------

        /// <summary>规约 AND 操作符重载（&amp; 运算符）</summary>
        public static Specification<T> operator &(Specification<T> left, Specification<T> right)
            => left.And(right);

        /// <summary>规约 OR 操作符重载（| 运算符）</summary>
        public static Specification<T> operator |(Specification<T> left, Specification<T> right)
            => left.Or(right);

        /// <summary>规约 NOT 操作符重载（! 运算符）</summary>
        public static Specification<T> operator !(Specification<T> spec)
            => spec.Not();
    }

    // ====================================================================
    // 组合规约实现（内部使用，由 And/Or/Not 方法创建）
    // ====================================================================

    /// <summary>
    /// AND 组合规约（两个规约均满足）。
    /// 使用表达式树组合，而非 lambda 调用，保证 EF Core 可翻译为 SQL。
    /// </summary>
    internal sealed class AndSpecification<T> : Specification<T>
    {
        private readonly Specification<T> _left;
        private readonly Specification<T> _right;

        public AndSpecification(Specification<T> left, Specification<T> right)
        {
            _left = left;
            _right = right;
        }

        public override Expression<Func<T, bool>> ToExpression()
        {
            // 构建参数共享的 AND 表达式：left(x) && right(x)
            var leftExpr = _left.ToExpression();
            var rightExpr = _right.ToExpression();

            // 替换右侧表达式的参数，使两个表达式共享同一个参数变量
            var parameter = leftExpr.Parameters[0];
            var rightBody = ExpressionParameterReplacer.Replace(rightExpr.Body, rightExpr.Parameters[0], parameter);

            return Expression.Lambda<Func<T, bool>>(
                Expression.AndAlso(leftExpr.Body, rightBody),
                parameter);
        }
    }

    /// <summary>
    /// OR 组合规约（两个规约任一满足）。
    /// </summary>
    internal sealed class OrSpecification<T> : Specification<T>
    {
        private readonly Specification<T> _left;
        private readonly Specification<T> _right;

        public OrSpecification(Specification<T> left, Specification<T> right)
        {
            _left = left;
            _right = right;
        }

        public override Expression<Func<T, bool>> ToExpression()
        {
            var leftExpr = _left.ToExpression();
            var rightExpr = _right.ToExpression();
            var parameter = leftExpr.Parameters[0];
            var rightBody = ExpressionParameterReplacer.Replace(rightExpr.Body, rightExpr.Parameters[0], parameter);

            return Expression.Lambda<Func<T, bool>>(
                Expression.OrElse(leftExpr.Body, rightBody),
                parameter);
        }
    }

    /// <summary>
    /// NOT 规约（对规约取反）。
    /// </summary>
    internal sealed class NotSpecification<T> : Specification<T>
    {
        private readonly Specification<T> _inner;

        public NotSpecification(Specification<T> inner)
        {
            _inner = inner;
        }

        public override Expression<Func<T, bool>> ToExpression()
        {
            var innerExpr = _inner.ToExpression();
            return Expression.Lambda<Func<T, bool>>(
                Expression.Not(innerExpr.Body),
                innerExpr.Parameters[0]);
        }
    }

    /// <summary>
    /// 表达式参数替换工具（用于组合表达式时统一参数变量）。
    /// 避免组合后的表达式包含多个同名但不同引用的参数变量（EF Core 无法翻译）。
    /// </summary>
    internal sealed class ExpressionParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParam;
        private readonly ParameterExpression _newParam;

        private ExpressionParameterReplacer(ParameterExpression oldParam, ParameterExpression newParam)
        {
            _oldParam = oldParam;
            _newParam = newParam;
        }

        /// <summary>
        /// 将表达式中的 oldParam 参数引用替换为 newParam。
        /// </summary>
        public static Expression Replace(Expression expression, ParameterExpression oldParam, ParameterExpression newParam)
            => new ExpressionParameterReplacer(oldParam, newParam).Visit(expression)!;

        protected override Expression VisitParameter(ParameterExpression node)
            => node == _oldParam ? _newParam : base.VisitParameter(node);
    }

    // ====================================================================
    // 具体规约实现（业务过滤条件）
    // ====================================================================

    /// <summary>
    /// 活动报警规约：只返回 IsActive=true 且未软删除的报警记录。
    /// </summary>
    public sealed class ActiveAlarmSpecification : Specification<AlarmRecord>
    {
        public override Expression<Func<AlarmRecord, bool>> ToExpression()
            => alarm => alarm.IsActive && !alarm.IsDeleted;
    }

    /// <summary>
    /// 报警等级规约：返回等级大于等于指定等级的报警记录。
    /// 用途：过滤出 Critical 及以上的紧急报警。
    /// </summary>
    public sealed class AlarmLevelSpecification : Specification<AlarmRecord>
    {
        private readonly AlarmLevel _minLevel;

        /// <param name="minLevel">最低报警等级（包含此等级）</param>
        public AlarmLevelSpecification(AlarmLevel minLevel)
        {
            _minLevel = minLevel;
        }

        public override Expression<Func<AlarmRecord, bool>> ToExpression()
            => alarm => alarm.Level >= _minLevel;
    }

    /// <summary>
    /// 报警类别规约：只返回指定类别的报警记录。
    /// </summary>
    public sealed class AlarmCategorySpecification : Specification<AlarmRecord>
    {
        private readonly AlarmCategory _category;

        public AlarmCategorySpecification(AlarmCategory category)
        {
            _category = category;
        }

        public override Expression<Func<AlarmRecord, bool>> ToExpression()
            => alarm => alarm.Category == _category;
    }

    /// <summary>
    /// 未确认活动报警规约：活动且尚未被操作员确认的报警（需要立即响应）。
    /// 使用组合规约演示：ActiveAlarm AND UnacknowledgedAlarm
    /// </summary>
    public sealed class UnacknowledgedActiveAlarmSpecification : Specification<AlarmRecord>
    {
        public override Expression<Func<AlarmRecord, bool>> ToExpression()
            => alarm => alarm.IsActive && alarm.AcknowledgedAt == null && !alarm.IsDeleted;
    }

    /// <summary>
    /// 用户角色规约：只返回指定角色的用户账户。
    /// 用途：查询所有工程师账户、管理员账户等。
    /// </summary>
    public sealed class UserRoleSpecification : Specification<UserAccount>
    {
        private readonly UserRole _role;

        public UserRoleSpecification(UserRole role)
        {
            _role = role;
        }

        public override Expression<Func<UserAccount, bool>> ToExpression()
            => user => user.Role == _role && !user.IsDeleted;
    }

    /// <summary>
    /// 激活配方规约：只返回 Active 状态且未删除的配方。
    /// </summary>
    public sealed class ActiveRecipeSpecification : Specification<RecipeData>
    {
        public override Expression<Func<RecipeData, bool>> ToExpression()
            => recipe => recipe.State == RecipeState.Active && !recipe.IsDeleted;
    }

    /// <summary>
    /// 按配方名称过滤规约：返回指定名称的所有版本配方。
    /// </summary>
    public sealed class RecipeByNameSpecification : Specification<RecipeData>
    {
        private readonly string _name;

        public RecipeByNameSpecification(string name)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public override Expression<Func<RecipeData, bool>> ToExpression()
            => recipe => recipe.Name == _name && !recipe.IsDeleted;
    }
}
