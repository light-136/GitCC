// ============================================================
// 文件：CircularInterpolator.cs
// 层级：硬件抽象层（Hardware Layer）> Motion > Interpolation
// 职责：2轴圆弧插补算法。
//       将圆弧路径微段化为直线段序列，每段再交给 LinearInterpolator 执行，
//       实现2轴（XY/XZ/YZ平面）的圆弧轨迹联动。
//
// 支持两种圆弧定义方式：
//   1. 圆心+角度：指定圆心坐标、起始角度、终止角度（或扫描角度）
//   2. 三点定圆：指定起点、终点、圆弧上任意一点，自动求解圆心和半径
//
// 算法原理（微段化）：
//   将圆弧按角度均匀分割为 N 段，每段为一小段弦线（近似为直线），
//   微段越小，轨迹越精确，但计算量越大。
//   推荐微段弦长 ≤ 0.1mm（即 DefaultChordLength = 0.1）。
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Hardware.Motion.Interpolation
{
    /// <summary>
    /// 圆弧插补平面枚举
    /// </summary>
    public enum ArcPlane
    {
        /// <summary>XY平面（第三轴Z不运动）</summary>
        XY = 0,
        /// <summary>XZ平面（第三轴Y不运动）</summary>
        XZ = 1,
        /// <summary>YZ平面（第三轴X不运动）</summary>
        YZ = 2
    }

    /// <summary>
    /// 圆弧微段 — 圆弧被分解为若干直线微段后的单段描述。
    /// </summary>
    public class ArcSegment
    {
        /// <summary>微段序号</summary>
        public int Index { get; set; }

        /// <summary>此微段起始角度（弧度）</summary>
        public double StartAngle { get; set; }

        /// <summary>此微段终止角度（弧度）</summary>
        public double EndAngle { get; set; }

        /// <summary>轴1（如X轴）目标坐标（mm）</summary>
        public double Axis1Position { get; set; }

        /// <summary>轴2（如Y轴）目标坐标（mm）</summary>
        public double Axis2Position { get; set; }

        /// <summary>此微段的弦长（mm）</summary>
        public double ChordLength { get; set; }

        /// <summary>此微段的进给速度（mm/s）</summary>
        public double FeedRate { get; set; }
    }

    /// <summary>
    /// 圆弧插补结果
    /// </summary>
    public class CircularInterpolationResult
    {
        /// <summary>圆心坐标（轴1方向，mm）</summary>
        public double CenterAxis1 { get; set; }

        /// <summary>圆心坐标（轴2方向，mm）</summary>
        public double CenterAxis2 { get; set; }

        /// <summary>圆弧半径（mm）</summary>
        public double Radius { get; set; }

        /// <summary>起始角度（弧度）</summary>
        public double StartAngle { get; set; }

        /// <summary>扫描角度（弧度，正=逆时针，负=顺时针）</summary>
        public double SweepAngle { get; set; }

        /// <summary>圆弧总长度（mm = |SweepAngle| × Radius）</summary>
        public double ArcLength { get; set; }

        /// <summary>微段总数</summary>
        public int SegmentCount { get; set; }

        /// <summary>规划总时间（s）</summary>
        public double TotalTime { get; set; }

        /// <summary>圆弧微段列表</summary>
        public List<ArcSegment> Segments { get; set; } = new();
    }

    /// <summary>
    /// 2轴圆弧插补器（XY/XZ/YZ平面）。
    /// 将圆弧路径微段化为若干直线弦线，供运动控制器逐段执行。
    ///
    /// 使用示例（圆心+角度方式）：
    ///   var arc = new CircularInterpolator();
    ///   var result = arc.CalculateFromCenter(
    ///       centerX: 50, centerY: 0,
    ///       startX: 0, startY: 0,
    ///       sweepAngle: Math.PI,   // 半圆
    ///       feedRate: 100);
    ///
    /// 使用示例（三点定圆方式）：
    ///   var result = arc.CalculateFromThreePoints(
    ///       p1: (0, 0), p2: (50, 50), p3: (100, 0),
    ///       feedRate: 100);
    /// </summary>
    public class CircularInterpolator
    {
        // ==================== 配置参数 ====================

        /// <summary>默认微段弦长（mm，越小圆弧越平滑）</summary>
        public double DefaultChordLength { get; set; } = 0.1;

        // ==================== 方式1：圆心+角度 ====================

        /// <summary>
        /// 通过圆心坐标和扫描角度定义圆弧并计算插补微段。
        /// </summary>
        /// <param name="centerAxis1">圆心在轴1方向的坐标（mm）</param>
        /// <param name="centerAxis2">圆心在轴2方向的坐标（mm）</param>
        /// <param name="startAxis1">起点轴1坐标（mm）</param>
        /// <param name="startAxis2">起点轴2坐标（mm）</param>
        /// <param name="sweepAngle">扫描角度（弧度，正=逆时针，负=顺时针）</param>
        /// <param name="feedRate">进给速度（mm/s）</param>
        /// <param name="chordLength">微段弦长（mm，null=使用默认值）</param>
        public CircularInterpolationResult CalculateFromCenter(
            double centerAxis1, double centerAxis2,
            double startAxis1, double startAxis2,
            double sweepAngle,
            double feedRate,
            double? chordLength = null)
        {
            if (feedRate <= 0) throw new ArgumentException($"进给速度必须大于0：{feedRate}");

            // ---- 计算半径 ----
            double dx = startAxis1 - centerAxis1;
            double dy = startAxis2 - centerAxis2;
            double radius = Math.Sqrt(dx * dx + dy * dy);

            if (radius < 1e-6) throw new ArgumentException("起点与圆心重合，半径为0");

            // ---- 起始角度 ----
            double startAngle = Math.Atan2(dy, dx);

            return CalculateInternal(
                centerAxis1, centerAxis2,
                radius, startAngle, sweepAngle,
                feedRate, chordLength ?? DefaultChordLength);
        }

        // ==================== 方式2：三点定圆 ====================

        /// <summary>
        /// 通过起点、终点、圆弧上任意一点定义圆弧。
        /// 内部自动计算圆心和半径，然后调用圆心+角度方式。
        /// </summary>
        /// <param name="startPoint">起点坐标 (轴1, 轴2)</param>
        /// <param name="midPoint">圆弧上的中间点（不能与起终点共线）</param>
        /// <param name="endPoint">终点坐标 (轴1, 轴2)</param>
        /// <param name="feedRate">进给速度（mm/s）</param>
        /// <param name="isClockwise">是否顺时针（true=顺时针，false=逆时针）</param>
        /// <param name="chordLength">微段弦长（mm）</param>
        public CircularInterpolationResult CalculateFromThreePoints(
            (double x, double y) startPoint,
            (double x, double y) midPoint,
            (double x, double y) endPoint,
            double feedRate,
            bool isClockwise = false,
            double? chordLength = null)
        {
            // ---- 求三点定圆的圆心和半径 ----
            var (cx, cy, radius) = SolveCircleFromThreePoints(startPoint, midPoint, endPoint);

            double startAngle = Math.Atan2(startPoint.y - cy, startPoint.x - cx);
            double endAngle = Math.Atan2(endPoint.y - cy, endPoint.x - cx);

            // ---- 计算扫描角度 ----
            double sweepAngle = endAngle - startAngle;

            // 根据顺逆时针方向调整扫描角度到正确范围
            if (isClockwise)
            {
                // 顺时针：扫描角度必须为负
                if (sweepAngle > 0) sweepAngle -= 2 * Math.PI;
            }
            else
            {
                // 逆时针：扫描角度必须为正
                if (sweepAngle < 0) sweepAngle += 2 * Math.PI;
            }

            return CalculateInternal(cx, cy, radius, startAngle, sweepAngle,
                feedRate, chordLength ?? DefaultChordLength);
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 圆弧插补核心计算（微段化）
        /// </summary>
        private static CircularInterpolationResult CalculateInternal(
            double cx, double cy,
            double radius, double startAngle, double sweepAngle,
            double feedRate, double chordLength)
        {
            double arcLength = Math.Abs(sweepAngle) * radius;

            if (arcLength < 1e-6)
            {
                return new CircularInterpolationResult
                {
                    CenterAxis1 = cx, CenterAxis2 = cy,
                    Radius = radius, StartAngle = startAngle,
                    SweepAngle = sweepAngle, ArcLength = 0,
                    SegmentCount = 0, TotalTime = 0
                };
            }

            // ---- 计算微段数量 ----
            // 按弦长计算对应的角度步长：chord = 2 * r * sin(dTheta/2)
            // => dTheta ≈ 2 * arcsin(chord / (2*r))（精确值）
            double dTheta = 2.0 * Math.Asin(Math.Min(chordLength / (2.0 * radius), 1.0));
            int n = (int)Math.Ceiling(Math.Abs(sweepAngle) / dTheta);
            n = Math.Max(n, 1);

            double actualDTheta = sweepAngle / n; // 实际每段角度步长（含方向符号）

            // ---- 生成微段列表 ----
            var segments = new List<ArcSegment>(n);
            double totalTime = 0;

            for (int i = 0; i < n; i++)
            {
                double angleEnd = startAngle + actualDTheta * (i + 1);
                double angleStart = startAngle + actualDTheta * i;

                double x2 = cx + radius * Math.Cos(angleEnd);
                double y2 = cy + radius * Math.Sin(angleEnd);

                // 弦长计算
                double x1 = cx + radius * Math.Cos(angleStart);
                double y1 = cy + radius * Math.Sin(angleStart);
                double chord = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

                totalTime += chord / feedRate;

                segments.Add(new ArcSegment
                {
                    Index = i,
                    StartAngle = angleStart,
                    EndAngle = angleEnd,
                    Axis1Position = x2,
                    Axis2Position = y2,
                    ChordLength = chord,
                    FeedRate = feedRate
                });
            }

            return new CircularInterpolationResult
            {
                CenterAxis1 = cx,
                CenterAxis2 = cy,
                Radius = radius,
                StartAngle = startAngle,
                SweepAngle = sweepAngle,
                ArcLength = arcLength,
                SegmentCount = n,
                TotalTime = totalTime,
                Segments = segments
            };
        }

        /// <summary>
        /// 三点定圆求解圆心坐标和半径（外接圆公式）。
        /// 使用垂直平分线法：两弦的垂直平分线的交点即为圆心。
        /// </summary>
        private static (double cx, double cy, double radius) SolveCircleFromThreePoints(
            (double x, double y) p1,
            (double x, double y) p2,
            (double x, double y) p3)
        {
            // 列方程组（利用三点到圆心距离相等）
            // 两个方程（消去r²）：
            // 2*(x2-x1)*cx + 2*(y2-y1)*cy = x2²-x1² + y2²-y1²
            // 2*(x3-x2)*cx + 2*(y3-y2)*cy = x3²-x2² + y3²-y2²
            double ax = 2 * (p2.x - p1.x), bx = 2 * (p2.y - p1.y);
            double cx_rhs = p2.x * p2.x - p1.x * p1.x + p2.y * p2.y - p1.y * p1.y;

            double ax2 = 2 * (p3.x - p2.x), bx2 = 2 * (p3.y - p2.y);
            double cx_rhs2 = p3.x * p3.x - p2.x * p2.x + p3.y * p3.y - p2.y * p2.y;

            double det = ax * bx2 - ax2 * bx;
            if (Math.Abs(det) < 1e-10)
                throw new ArgumentException("三点共线，无法定圆");

            double cx = (cx_rhs * bx2 - cx_rhs2 * bx) / det;
            double cy = (ax * cx_rhs2 - ax2 * cx_rhs) / det;

            double radius = Math.Sqrt((p1.x - cx) * (p1.x - cx) + (p1.y - cy) * (p1.y - cy));
            return (cx, cy, radius);
        }
    }
}
