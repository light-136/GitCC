// ============================================================
// 文件：CalibrationService.cs
// 用途：标定服务 — 9点仿射标定 + 最小二乘求解
// 设计思路：
//   相机标定用于建立像素坐标与物理坐标之间的映射关系。
//   本实现采用仿射变换模型（6参数），通过9个标定点
//   （3×3网格）建立超定方程组，用最小二乘法求解。
//
//   仿射变换：
//     Xw = a11·Xp + a12·Yp + a13
//     Yw = a21·Xp + a22·Yp + a23
//   其中 (Xp,Yp)=像素坐标, (Xw,Yw)=物理坐标
//
//   9点标定：
//     - 采集9个标定点的像素坐标和物理坐标
//     - 构建超定方程组（9个方程，3个未知数×2组）
//     - 最小二乘求解 2×3 仿射矩阵
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Modules.Vision
{
    /// <summary>
    /// 标定点对 — 像素坐标与物理坐标的对应关系。
    /// </summary>
    public class CalibrationPointPair
    {
        /// <summary>像素 X 坐标。</summary>
        public double PixelX { get; set; }

        /// <summary>像素 Y 坐标。</summary>
        public double PixelY { get; set; }

        /// <summary>物理 X 坐标（mm）。</summary>
        public double WorldX { get; set; }

        /// <summary>物理 Y 坐标（mm）。</summary>
        public double WorldY { get; set; }
    }

    /// <summary>
    /// 标定服务 — 执行相机标定和坐标转换。
    ///
    /// 标定流程：
    ///   1. 采集 N 个标定点（像素坐标 + 物理坐标）
    ///   2. 调用 Calibrate() 计算仿射变换矩阵
    ///   3. 使用 PixelToWorld() 将检测结果从像素转换为物理坐标
    ///
    /// 最少需要3个非共线的标定点，推荐9个点（3×3网格）以提高精度。
    /// </summary>
    public class CalibrationService
    {
        /// <summary>当前标定数据。</summary>
        public CalibrationData? CurrentCalibration { get; private set; }

        /// <summary>是否已标定。</summary>
        public bool IsCalibrated => CurrentCalibration != null;

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>
        /// 执行标定 — 从标定点对计算仿射变换矩阵。
        ///
        /// 数学原理：
        ///   对于每个标定点 i：
        ///     Xw[i] = a11·Xp[i] + a12·Yp[i] + a13
        ///     Yw[i] = a21·Xp[i] + a22·Yp[i] + a23
        ///
        ///   构建矩阵方程 A·x = b：
        ///     A = [Xp[0] Yp[0] 1; Xp[1] Yp[1] 1; ...]  (N×3)
        ///     x1 = [a11; a12; a13]  → b1 = [Xw[0]; Xw[1]; ...]
        ///     x2 = [a21; a22; a23]  → b2 = [Yw[0]; Yw[1]; ...]
        ///
        ///   最小二乘解：x = (AᵀA)⁻¹ · Aᵀb
        /// </summary>
        /// <param name="points">标定点对列表（至少3个）。</param>
        /// <returns>标定数据（包含变换矩阵和误差）。</returns>
        public CalibrationData Calibrate(List<CalibrationPointPair> points)
        {
            if (points.Count < 3)
                throw new ArgumentException("标定至少需要3个点对");

            int n = points.Count;
            Log($"[标定] 开始标定，{n} 个标定点");

            // 构建 AᵀA (3×3) 和 Aᵀb (3×1 ×2组)
            double sumXp = 0, sumYp = 0, sumXp2 = 0, sumYp2 = 0, sumXpYp = 0;
            double sumXw = 0, sumYw = 0;
            double sumXpXw = 0, sumYpXw = 0;
            double sumXpYw = 0, sumYpYw = 0;

            foreach (var p in points)
            {
                sumXp += p.PixelX; sumYp += p.PixelY;
                sumXp2 += p.PixelX * p.PixelX;
                sumYp2 += p.PixelY * p.PixelY;
                sumXpYp += p.PixelX * p.PixelY;
                sumXw += p.WorldX; sumYw += p.WorldY;
                sumXpXw += p.PixelX * p.WorldX;
                sumYpXw += p.PixelY * p.WorldX;
                sumXpYw += p.PixelX * p.WorldY;
                sumYpYw += p.PixelY * p.WorldY;
            }

            // AᵀA 矩阵
            double[,] ata =
            {
                { sumXp2, sumXpYp, sumXp },
                { sumXpYp, sumYp2, sumYp },
                { sumXp, sumYp, n }
            };

            // AᵀbX 和 AᵀbY 向量
            double[] atbX = { sumXpXw, sumYpXw, sumXw };
            double[] atbY = { sumXpYw, sumYpYw, sumYw };

            // 求解 AᵀA · x = Aᵀb（高斯消元法）
            var coeffX = SolveLinearSystem3x3(ata, atbX);
            var coeffY = SolveLinearSystem3x3(ata, atbY);

            if (coeffX == null || coeffY == null)
            {
                throw new InvalidOperationException("标定方程无解（标定点可能共线）");
            }

            // 构建 2×3 变换矩阵
            var transform = new double[,]
            {
                { coeffX[0], coeffX[1], coeffX[2] },
                { coeffY[0], coeffY[1], coeffY[2] }
            };

            // 计算标定误差
            double maxError = 0, sumError = 0;
            foreach (var p in points)
            {
                double wx = coeffX[0] * p.PixelX + coeffX[1] * p.PixelY + coeffX[2];
                double wy = coeffY[0] * p.PixelX + coeffY[1] * p.PixelY + coeffY[2];
                double err = Math.Sqrt((wx - p.WorldX) * (wx - p.WorldX) +
                                       (wy - p.WorldY) * (wy - p.WorldY));
                sumError += err;
                maxError = Math.Max(maxError, err);
            }

            var calibData = new CalibrationData
            {
                TransformMatrix = transform,
                MeanError = sumError / n,
                MaxError = maxError,
                PointCount = n
            };

            CurrentCalibration = calibData;

            Log($"[标定] 完成，平均误差={calibData.MeanError:F4}mm，最大误差={maxError:F4}mm");
            return calibData;
        }

        /// <summary>
        /// 像素坐标 → 物理坐标。
        /// </summary>
        public (double WorldX, double WorldY) PixelToWorld(double pixelX, double pixelY)
        {
            if (CurrentCalibration == null)
                throw new InvalidOperationException("未标定");

            var m = CurrentCalibration.TransformMatrix;
            double wx = m[0, 0] * pixelX + m[0, 1] * pixelY + m[0, 2];
            double wy = m[1, 0] * pixelX + m[1, 1] * pixelY + m[1, 2];
            return (wx, wy);
        }

        /// <summary>
        /// 生成默认9点标定网格（3×3）。
        /// </summary>
        /// <param name="imageWidth">图像宽度。</param>
        /// <param name="imageHeight">图像高度。</param>
        /// <param name="physicalWidth">物理宽度（mm）。</param>
        /// <param name="physicalHeight">物理高度（mm）。</param>
        public static List<CalibrationPointPair> GenerateDefaultGrid(
            int imageWidth, int imageHeight,
            double physicalWidth, double physicalHeight)
        {
            var points = new List<CalibrationPointPair>();

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    double px = imageWidth * (col + 1) / 4.0;
                    double py = imageHeight * (row + 1) / 4.0;
                    double wx = physicalWidth * (col + 1) / 4.0;
                    double wy = physicalHeight * (row + 1) / 4.0;

                    points.Add(new CalibrationPointPair
                    {
                        PixelX = px, PixelY = py,
                        WorldX = wx, WorldY = wy
                    });
                }
            }

            return points;
        }

        /// <summary>
        /// 求解 3×3 线性方程组（高斯消元法）。
        /// </summary>
        private static double[]? SolveLinearSystem3x3(double[,] matrix, double[] rhs)
        {
            // 复制避免修改原矩阵
            var a = new double[3, 4];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++) a[i, j] = matrix[i, j];
                a[i, 3] = rhs[i];
            }

            // 前向消元
            for (int col = 0; col < 3; col++)
            {
                // 选主元
                int maxRow = col;
                for (int row = col + 1; row < 3; row++)
                {
                    if (Math.Abs(a[row, col]) > Math.Abs(a[maxRow, col]))
                        maxRow = row;
                }

                // 交换行
                if (maxRow != col)
                {
                    for (int j = 0; j < 4; j++)
                        (a[col, j], a[maxRow, j]) = (a[maxRow, j], a[col, j]);
                }

                if (Math.Abs(a[col, col]) < 1e-12) return null;

                // 消元
                for (int row = col + 1; row < 3; row++)
                {
                    double factor = a[row, col] / a[col, col];
                    for (int j = col; j < 4; j++)
                        a[row, j] -= factor * a[col, j];
                }
            }

            // 回代
            var result = new double[3];
            for (int i = 2; i >= 0; i--)
            {
                result[i] = a[i, 3];
                for (int j = i + 1; j < 3; j++)
                    result[i] -= a[i, j] * result[j];
                result[i] /= a[i, i];
            }

            return result;
        }

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);
    }
}
