// ============================================================
// 文件：CircularInterpolator.cs
// 用途：圆弧插补算法 — 将圆弧运动分解为微线段序列
// 设计思路：
//   CNC 系统中圆弧运动（G2/G3）需要分解为大量微小直线段，
//   再由多轴控制器逐段执行。本类支持两种圆弧定义方式：
//   1. I/J 模式：圆心 = 起点 + (I, J) 偏移
//   2. R 模式：正R=劣弧，负R=优弧
//   通过等角度步进生成微线段点序列。
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 圆弧插补器 — 将圆弧运动分解为微线段序列。
    /// 支持 I/J 圆心偏移模式和 R 半径模式。
    /// </summary>
    public class CircularInterpolator
    {
        /// <summary>
        /// 根据圆弧参数生成微线段点序列。
        /// </summary>
        /// <param name="startX">起点 X 坐标（mm）。</param>
        /// <param name="startY">起点 Y 坐标（mm）。</param>
        /// <param name="endX">终点 X 坐标（mm）。</param>
        /// <param name="endY">终点 Y 坐标（mm）。</param>
        /// <param name="arcParams">圆弧参数（I/J 或 R 模式）。</param>
        /// <param name="feedRate">进给速度（mm/min）。</param>
        /// <param name="stepSizeMm">微线段步长（mm）。</param>
        /// <returns>插补点序列。</returns>
        public List<InterpolationPoint> GenerateArcPoints(
            double startX, double startY, double endX, double endY,
            ArcParameters arcParams, double feedRate, double stepSizeMm = 0.1)
        {
            var points = new List<InterpolationPoint>();

            // 步长保护
            if (stepSizeMm <= 0) stepSizeMm = 0.1;

            double cx, cy, radius;

            if (arcParams.UseRadiusMode)
            {
                // ===== R 模式：由半径计算圆心 =====
                // 正R = 劣弧（圆心角 <= 180°），负R = 优弧（圆心角 > 180°）
                (cx, cy, radius) = ComputeCenterFromRadius(
                    startX, startY, endX, endY,
                    arcParams.Radius, arcParams.IsClockwise);
            }
            else
            {
                // ===== I/J 模式：圆心 = 起点 + (I, J) =====
                cx = startX + arcParams.CenterI;
                cy = startY + arcParams.CenterJ;
                radius = Math.Sqrt(
                    (startX - cx) * (startX - cx) +
                    (startY - cy) * (startY - cy));
            }

            // 零半径保护
            if (radius < 1e-6) return points;

            // 计算起始角和终止角（弧度）
            double startAngle = Math.Atan2(startY - cy, startX - cx);
            double endAngle = Math.Atan2(endY - cy, endX - cx);

            // 处理起终点重合的情况 → 整圆
            bool isFullCircle = Math.Abs(startX - endX) < 1e-6 &&
                                Math.Abs(startY - endY) < 1e-6;

            // 计算扫过的角度
            double sweep;
            if (isFullCircle)
            {
                // 起终点重合 → 整圆（360°）
                sweep = 2.0 * Math.PI;
            }
            else if (arcParams.IsClockwise)
            {
                // 顺时针：角度递减
                sweep = startAngle - endAngle;
                if (sweep <= 0) sweep += 2.0 * Math.PI;
            }
            else
            {
                // 逆时针：角度递增
                sweep = endAngle - startAngle;
                if (sweep <= 0) sweep += 2.0 * Math.PI;
            }

            // 根据步长计算角度步进
            // 弧长 = radius × sweep，步数 = 弧长 / stepSize
            double arcLength = radius * sweep;
            int steps = Math.Max(1, (int)Math.Ceiling(arcLength / stepSizeMm));
            double angleStep = sweep / steps;

            // 生成微线段点
            for (int i = 1; i <= steps; i++)
            {
                double angle;
                if (arcParams.IsClockwise)
                    angle = startAngle - angleStep * i; // 顺时针角度递减
                else
                    angle = startAngle + angleStep * i; // 逆时针角度递增

                double x = cx + radius * Math.Cos(angle);
                double y = cy + radius * Math.Sin(angle);

                // 最后一个点强制对齐终点（消除累积误差）
                if (i == steps && !isFullCircle)
                {
                    x = endX;
                    y = endY;
                }

                points.Add(new InterpolationPoint
                {
                    AxisTargets = new Dictionary<string, double> { ["X"] = x, ["Y"] = y },
                    FeedRate = feedRate / 60.0 // mm/min → mm/s
                });
            }

            return points;
        }

        /// <summary>
        /// 生成整圆的微线段点序列。
        /// </summary>
        /// <param name="centerX">圆心 X 坐标。</param>
        /// <param name="centerY">圆心 Y 坐标。</param>
        /// <param name="radius">半径（mm）。</param>
        /// <param name="clockwise">是否顺时针。</param>
        /// <param name="feedRate">进给速度（mm/min）。</param>
        /// <param name="stepSizeMm">微线段步长（mm）。</param>
        /// <returns>插补点序列（首尾相连的整圆）。</returns>
        public List<InterpolationPoint> GenerateFullCircle(
            double centerX, double centerY, double radius,
            bool clockwise, double feedRate, double stepSizeMm = 0.1)
        {
            if (radius < 1e-6 || stepSizeMm <= 0)
                return new List<InterpolationPoint>();

            // 起点在圆的 0° 位置（正X方向）
            double startX = centerX + radius;
            double startY = centerY;

            var arcParams = new ArcParameters
            {
                UseRadiusMode = false,
                CenterI = centerX - startX, // = -radius
                CenterJ = centerY - startY, // = 0
                IsClockwise = clockwise
            };

            // 起终点相同 → GenerateArcPoints 会识别为整圆
            return GenerateArcPoints(startX, startY, startX, startY,
                                     arcParams, feedRate, stepSizeMm);
        }

        /// <summary>
        /// 由半径和起终点计算圆心坐标。
        /// <para>
        /// 算法原理：
        /// 1. 计算起终点中点 M 和连线距离 d
        /// 2. 圆心到中点的距离 h = sqrt(R² - (d/2)²)
        /// 3. 圆心在中点法线方向偏移 h
        /// 4. 正R取劣弧侧圆心，负R取优弧侧圆心
        /// </para>
        /// </summary>
        private static (double cx, double cy, double radius) ComputeCenterFromRadius(
            double x1, double y1, double x2, double y2, double r, bool clockwise)
        {
            double absR = Math.Abs(r);
            double dx = x2 - x1;
            double dy = y2 - y1;
            double d = Math.Sqrt(dx * dx + dy * dy); // 起终点距离

            // 距离为零或半径太小，无法构成圆弧
            if (d < 1e-6 || absR < d / 2.0 - 1e-6)
                return ((x1 + x2) / 2, (y1 + y2) / 2, absR);

            // 中点坐标
            double mx = (x1 + x2) / 2.0;
            double my = (y1 + y2) / 2.0;

            // 圆心到中点的距离
            double h = Math.Sqrt(Math.Max(0, absR * absR - (d / 2.0) * (d / 2.0)));

            // 中点法线方向单位向量（垂直于起终点连线）
            double nx = -dy / d;
            double ny = dx / d;

            // 确定圆心在法线的哪一侧
            // 正R（劣弧）：顺时针时圆心在右侧，逆时针时在左侧
            // 负R（优弧）：方向相反
            bool flipSide = (r < 0) ^ clockwise;
            double sign = flipSide ? -1.0 : 1.0;

            double cx = mx + sign * h * nx;
            double cy = my + sign * h * ny;

            return (cx, cy, absR);
        }
    }
}
