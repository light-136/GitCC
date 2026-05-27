// ============================================================
// 文件：Position3D.cs
// 层次：领域层 (Domain Layer) — 值对象
// 职责：表示三维空间中的一个点坐标（X, Y, Z），单位由上下文决定（mm 或 pulse）
// 设计思路：
//   值对象（Value Object）是 DDD 中没有身份标识的不可变对象，
//   其相等性由属性值决定而非引用。Position3D 封装了三维坐标的
//   数学运算，使运动控制代码更具表达力。
//   使用 readonly record struct 实现：
//     1. 不可变性（编译时强制，防止意外修改坐标）
//     2. 栈分配（值类型，避免 GC 压力，运动控制循环中频繁创建销毁）
//     3. 自动的结构相等比较（record 特性，无需手写 Equals）
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Domain.ValueObjects
{
    /// <summary>
    /// 三维位置值对象，表示空间中的点坐标 (X, Y, Z)。
    /// 单位上下文：运动控制场景中通常为毫米（mm）或脉冲数（pulse），
    /// 由使用方在注释中说明单位，此类本身不耦合单位。
    /// </summary>
    public readonly record struct Position3D
    {
        // ----------------------------------------------------------------
        // 坐标属性（只读，创建后不可修改）
        // ----------------------------------------------------------------

        /// <summary>X 轴坐标值（通常对应水平方向）</summary>
        public double X { get; init; }

        /// <summary>Y 轴坐标值（通常对应垂直或进给方向）</summary>
        public double Y { get; init; }

        /// <summary>Z 轴坐标值（通常对应高度/深度方向）</summary>
        public double Z { get; init; }

        // ----------------------------------------------------------------
        // 构造函数
        // ----------------------------------------------------------------

        /// <summary>
        /// 创建三维坐标值对象。
        /// </summary>
        /// <param name="x">X 轴坐标</param>
        /// <param name="y">Y 轴坐标</param>
        /// <param name="z">Z 轴坐标</param>
        public Position3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// 创建二维坐标（Z 轴默认为 0），用于平面运动场景简化构造。
        /// </summary>
        /// <param name="x">X 轴坐标</param>
        /// <param name="y">Y 轴坐标</param>
        public Position3D(double x, double y) : this(x, y, 0.0) { }

        // ----------------------------------------------------------------
        // 常用静态工厂
        // ----------------------------------------------------------------

        /// <summary>坐标系原点 (0, 0, 0)，常用于回零后的机械零点</summary>
        public static readonly Position3D Zero = new(0.0, 0.0, 0.0);

        /// <summary>创建仅有 X 坐标的位置（Y=0, Z=0），用于单轴运动场景</summary>
        public static Position3D FromX(double x) => new(x, 0.0, 0.0);

        /// <summary>创建仅有 Y 坐标的位置（X=0, Z=0），用于单轴运动场景</summary>
        public static Position3D FromY(double y) => new(0.0, y, 0.0);

        /// <summary>创建仅有 Z 坐标的位置（X=0, Y=0），用于升降轴场景</summary>
        public static Position3D FromZ(double z) => new(0.0, 0.0, z);

        // ----------------------------------------------------------------
        // 算术运算符重载
        // 运算返回新的 Position3D 实例，不修改原始值（不可变语义）
        // ----------------------------------------------------------------

        /// <summary>
        /// 坐标相加：计算两个坐标的分量之和。
        /// 典型用法：绝对位置 + 偏移量 = 目标位置
        /// </summary>
        public static Position3D operator +(Position3D left, Position3D right)
            => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        /// <summary>
        /// 坐标相减：计算两个坐标的分量之差。
        /// 典型用法：目标位置 - 当前位置 = 需要移动的增量
        /// </summary>
        public static Position3D operator -(Position3D left, Position3D right)
            => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        /// <summary>
        /// 坐标取反：反转所有坐标分量的符号。
        /// 典型用法：回退到反方向偏移量
        /// </summary>
        public static Position3D operator -(Position3D pos)
            => new(-pos.X, -pos.Y, -pos.Z);

        /// <summary>
        /// 标量乘法：将所有坐标分量乘以一个标量因子。
        /// 典型用法：将插补路径按比例缩放，或单位换算（mm -> pulse）
        /// </summary>
        /// <param name="pos">被缩放的位置</param>
        /// <param name="scalar">缩放因子</param>
        public static Position3D operator *(Position3D pos, double scalar)
            => new(pos.X * scalar, pos.Y * scalar, pos.Z * scalar);

        /// <summary>
        /// 标量乘法（交换律重载，支持 scalar * position 写法）
        /// </summary>
        public static Position3D operator *(double scalar, Position3D pos)
            => pos * scalar;

        /// <summary>
        /// 标量除法：将所有坐标分量除以一个标量因子。
        /// </summary>
        /// <exception cref="DivideByZeroException">当 scalar 为零时抛出</exception>
        public static Position3D operator /(Position3D pos, double scalar)
        {
            if (scalar == 0.0)
                throw new DivideByZeroException("位置坐标除法不允许除数为零。");
            return new(pos.X / scalar, pos.Y / scalar, pos.Z / scalar);
        }

        // ----------------------------------------------------------------
        // 距离计算方法
        // ----------------------------------------------------------------

        /// <summary>
        /// 计算到另一个位置的欧氏距离（三维直线距离）。
        /// 公式：sqrt((x2-x1)² + (y2-y1)² + (z2-z1)²)
        /// 典型用法：估算运动时间（距离 / 速度）、碰撞检测预判
        /// </summary>
        /// <param name="other">目标位置</param>
        /// <returns>两点之间的直线距离，单位与坐标单位一致</returns>
        public double Distance(Position3D other)
        {
            var dx = X - other.X;
            var dy = Y - other.Y;
            var dz = Z - other.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// 计算到原点的距离（即向量模长）。
        /// </summary>
        public double Magnitude() => Distance(Zero);

        /// <summary>
        /// 计算到另一个位置的 XY 平面投影距离（忽略 Z 轴）。
        /// 典型用法：平面运动时间估算（Z 轴独立运动时）
        /// </summary>
        public double Distance2D(Position3D other)
        {
            var dx = X - other.X;
            var dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 线性插值：在当前位置和目标位置之间按 t（0~1）插值。
        /// t=0 时返回当前位置，t=1 时返回目标位置。
        /// 典型用法：运动路径的中间点采样、动画预览
        /// </summary>
        /// <param name="target">目标位置</param>
        /// <param name="t">插值参数（范围 0.0 ~ 1.0）</param>
        public Position3D Lerp(Position3D target, double t)
        {
            // 限制 t 在 [0,1] 范围，防止超出路径
            t = Math.Clamp(t, 0.0, 1.0);
            return new(
                X + (target.X - X) * t,
                Y + (target.Y - Y) * t,
                Z + (target.Z - Z) * t
            );
        }

        // ----------------------------------------------------------------
        // 字符串表示
        // ----------------------------------------------------------------

        /// <summary>
        /// 返回调试友好的坐标字符串，保留 3 位小数。
        /// 示例输出：(X=123.456, Y=78.900, Z=0.000)
        /// </summary>
        public override string ToString()
            => $"(X={X:F3}, Y={Y:F3}, Z={Z:F3})";

        /// <summary>
        /// 以指定小数位数格式化坐标字符串。
        /// </summary>
        /// <param name="decimals">小数位数（0~15）</param>
        public string ToString(int decimals)
        {
            var fmt = $"F{decimals}";
            return $"(X={X.ToString(fmt)}, Y={Y.ToString(fmt)}, Z={Z.ToString(fmt)})";
        }
    }
}
