// ============================================================
// 文件：MeasurementTools.cs
// 用途：测量工具 — 距离/角度/圆拟合(Kasa)/直线拟合
// 设计思路：
//   工业视觉中常需要对检测到的特征进行几何测量。
//   本文件实现四种基础测量工具：
//   1. 距离测量：两点间欧氏距离
//   2. 角度测量：三点构成的夹角
//   3. 圆拟合（Kasa法）：从点集拟合圆心和半径
//   4. 直线拟合（最小二乘法）：从点集拟合直线方程
//
//   Kasa 圆拟合原理：
//     将圆方程 (x-a)²+(y-b)²=r² 展开为线性形式
//     x²+y² = 2ax + 2by + (r²-a²-b²)
//     令 c = r²-a²-b²，转化为最小二乘问题
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Modules.Vision
{
    /// <summary>
    /// 测量工具集 — 提供工业视觉中常用的几何测量方法。
    /// </summary>
    public static class MeasurementTools
    {
        /// <summary>
        /// 计算两点间距离。
        /// </summary>
        public static MeasurementResult MeasureDistance(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            return new MeasurementResult
            {
                Type = MeasurementType.Distance,
                Value = distance,
                Description = "px"
            };
        }

        /// <summary>
        /// 计算三点构成的夹角（度）。
        /// 角度为从 P1-P2 到 P3-P2 的夹角，P2 为顶点。
        /// </summary>
        public static MeasurementResult MeasureAngle(
            double x1, double y1, double x2, double y2, double x3, double y3)
        {
            // 向量 P2→P1 和 P2→P3
            double v1x = x1 - x2, v1y = y1 - y2;
            double v2x = x3 - x2, v2y = y3 - y2;

            double dot = v1x * v2x + v1y * v2y;
            double len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            double len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

            double cosAngle = (len1 > 0 && len2 > 0)
                ? Math.Clamp(dot / (len1 * len2), -1.0, 1.0) : 0;
            double angleDeg = Math.Acos(cosAngle) * 180.0 / Math.PI;

            return new MeasurementResult
            {
                Type = MeasurementType.Angle,
                Value = angleDeg,
                Description = "°"
            };
        }

        /// <summary>
        /// Kasa 圆拟合 — 从点集拟合圆的圆心和半径。
        ///
        /// 原理：
        ///   将圆方程展开为线性形式：
        ///   x² + y² = 2a·x + 2b·y + c  (其中 c = r² - a² - b²)
        ///   构建超定方程组 A·[a, b, c]ᵀ = d
        ///   用最小二乘法求解：[a, b, c] = (AᵀA)⁻¹ · Aᵀd
        ///
        /// 最少需要3个点。
        /// </summary>
        /// <param name="points">点集 (x, y) 列表。</param>
        /// <returns>拟合结果（Value=半径，附加信息包含圆心坐标）。</returns>
        public static (double CenterX, double CenterY, double Radius, double Error) FitCircle(
            List<(double X, double Y)> points)
        {
            if (points.Count < 3)
                throw new ArgumentException("圆拟合至少需要3个点");

            int n = points.Count;

            // 构建法方程 AᵀA 和 Aᵀd
            // A 矩阵的每行：[2x, 2y, 1]
            // d 向量的每行：x² + y²
            double sumX = 0, sumY = 0, sumX2 = 0, sumY2 = 0;
            double sumXY = 0, sumX3 = 0, sumY3 = 0;
            double sumX2Y = 0, sumXY2 = 0;

            foreach (var (x, y) in points)
            {
                double x2 = x * x, y2 = y * y;
                sumX += x; sumY += y;
                sumX2 += x2; sumY2 += y2;
                sumXY += x * y;
                sumX3 += x2 * x; sumY3 += y2 * y;
                sumX2Y += x2 * y; sumXY2 += x * y2;
            }

            // AᵀA (3×3 对称矩阵)
            double a11 = 4 * sumX2, a12 = 4 * sumXY, a13 = 2 * sumX;
            double a22 = 4 * sumY2, a23 = 2 * sumY;
            double a33 = n;

            // Aᵀd (3×1 向量)
            double b1 = 2 * (sumX3 + sumXY2);
            double b2 = 2 * (sumX2Y + sumY3);
            double b3 = sumX2 + sumY2;

            // 解 3×3 线性方程组（克莱姆法则）
            double det = a11 * (a22 * a33 - a23 * a23)
                       - a12 * (a12 * a33 - a23 * a13)
                       + a13 * (a12 * a23 - a22 * a13);

            if (Math.Abs(det) < 1e-10)
                return (0, 0, 0, double.MaxValue);

            double a = (b1 * (a22 * a33 - a23 * a23) -
                        a12 * (b2 * a33 - a23 * b3) +
                        a13 * (b2 * a23 - a22 * b3)) / det;

            double b = (a11 * (b2 * a33 - a23 * b3) -
                        b1 * (a12 * a33 - a23 * a13) +
                        a13 * (a12 * b3 - b2 * a13)) / det;

            double c = (a11 * (a22 * b3 - b2 * a23) -
                        a12 * (a12 * b3 - b2 * a13) +
                        b1 * (a12 * a23 - a22 * a13)) / det;

            double cx = a;
            double cy = b;
            double r = Math.Sqrt(c + a * a + b * b);

            // 计算拟合误差（均方根误差）
            double sumErr = 0;
            foreach (var (x, y) in points)
            {
                double dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                double err = dist - r;
                sumErr += err * err;
            }
            double rmse = Math.Sqrt(sumErr / n);

            return (cx, cy, r, rmse);
        }

        /// <summary>
        /// 最小二乘直线拟合 — y = kx + b。
        ///
        /// 正规方程：
        ///   k = (n·Σxy - Σx·Σy) / (n·Σx² - (Σx)²)
        ///   b = (Σy - k·Σx) / n
        /// </summary>
        /// <param name="points">点集。</param>
        /// <returns>(斜率k, 截距b, 拟合误差RMSE)。</returns>
        public static (double K, double B, double Error) FitLine(
            List<(double X, double Y)> points)
        {
            if (points.Count < 2)
                throw new ArgumentException("直线拟合至少需要2个点");

            int n = points.Count;
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

            foreach (var (x, y) in points)
            {
                sumX += x; sumY += y;
                sumXY += x * y; sumX2 += x * x;
            }

            double denom = n * sumX2 - sumX * sumX;
            if (Math.Abs(denom) < 1e-10)
                return (double.MaxValue, 0, double.MaxValue); // 垂直线

            double k = (n * sumXY - sumX * sumY) / denom;
            double b = (sumY - k * sumX) / n;

            // RMSE
            double sumErr = 0;
            foreach (var (x, y) in points)
            {
                double err = y - (k * x + b);
                sumErr += err * err;
            }
            double rmse = Math.Sqrt(sumErr / n);

            return (k, b, rmse);
        }

        /// <summary>
        /// 点到直线距离。
        /// 直线方程：ax + by + c = 0
        /// 距离 = |ax₀ + by₀ + c| / sqrt(a² + b²)
        /// </summary>
        public static double PointToLineDistance(
            double px, double py, double a, double b, double c)
        {
            return Math.Abs(a * px + b * py + c) / Math.Sqrt(a * a + b * b);
        }
    }
}
