// ============================================================
// 文件：CalibrationService.cs
// 层级：硬件抽象层（Hardware Layer）> Vision > Calibration
// 职责：相机标定服务。
//       实现九点标定（最小二乘法求解2×3仿射变换矩阵），
//       提供像素坐标↔机械坐标的双向转换，
//       以及标定精度评估（RMS误差计算）和标定数据持久化。
//
// 标定原理（2D仿射变换）：
//   机械坐标 (wx, wy) = M * [px, py, 1]ᵀ
//   其中 M 是 2×3 仿射矩阵：
//     [wx]   [m00  m01  m02]   [px]
//     [wy] = [m10  m11  m12] * [py]
//                              [ 1]
//
//   利用 N 个标定点（像素坐标和对应机械坐标），
//   用最小二乘法求解 M 的6个参数。
//
// 九点标定布局（推荐）：
//   在视野范围内均匀分布9个标定点（3×3网格），
//   机器人依次移动到每个点，记录机械坐标和视觉识别的像素坐标，
//   输入本服务完成标定。
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using System.Text.Json;
using SmartIndustry.Domain.Models;

namespace SmartIndustry.Hardware.Vision.Calibration
{
    /// <summary>
    /// 标定点对 — 对应一个标定点的像素坐标和机械坐标。
    /// </summary>
    public class CalibrationPoint
    {
        /// <summary>点编号</summary>
        public int Index { get; set; }

        /// <summary>像素坐标X（相机图像中的位置，像素）</summary>
        public double PixelX { get; set; }

        /// <summary>像素坐标Y（相机图像中的位置，像素）</summary>
        public double PixelY { get; set; }

        /// <summary>机械坐标X（机器人/轴实际位置，mm）</summary>
        public double WorldX { get; set; }

        /// <summary>机械坐标Y（机器人/轴实际位置，mm）</summary>
        public double WorldY { get; set; }
    }

    /// <summary>
    /// 标定精度评估结果
    /// </summary>
    public class CalibrationAccuracyReport
    {
        /// <summary>RMS 误差（mm，均方根误差，越小越好）</summary>
        public double RmsError { get; set; }

        /// <summary>最大单点误差（mm）</summary>
        public double MaxError { get; set; }

        /// <summary>平均误差（mm）</summary>
        public double MeanError { get; set; }

        /// <summary>各标定点的残差（mm）</summary>
        public List<double> PointResiduals { get; set; } = new();

        /// <summary>标定是否合格（RMS < 指定阈值）</summary>
        public bool IsAcceptable { get; set; }

        /// <summary>评估时间</summary>
        public DateTime EvaluatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 相机标定服务。
    /// 提供九点标定、坐标转换、精度评估和数据持久化功能。
    ///
    /// 使用流程：
    ///   1. 在视野内摆放9点标定板
    ///   2. 采集每个标定点的像素坐标（视觉识别）和机械坐标（编码器）
    ///   3. 调用 Calibrate(points) 求解仿射矩阵
    ///   4. 调用 EvaluateAccuracy() 评估标定精度
    ///   5. 调用 SaveCalibration(path) 保存标定数据
    ///   6. 后续使用 PixelToWorld / WorldToPixel 进行坐标转换
    /// </summary>
    public class CalibrationService
    {
        // ==================== 私有字段 ====================

        /// <summary>当前有效的标定数据</summary>
        private CalibrationData? _calibrationData;

        /// <summary>标定使用的原始点对（保存用于重新评估）</summary>
        private List<CalibrationPoint> _calibrationPoints = new();

        // ==================== 公开属性 ====================

        /// <summary>是否已完成有效标定</summary>
        public bool IsCalibrated => _calibrationData?.IsValid == true;

        /// <summary>当前标定数据（未标定时为 null）</summary>
        public CalibrationData? CalibrationData => _calibrationData;

        // ==================== 标定计算 ====================

        /// <summary>
        /// 使用 N 个标定点对求解 2×3 仿射变换矩阵（最小二乘法）。
        /// 推荐使用9点以上，均匀分布在视野范围内。
        /// </summary>
        /// <param name="points">标定点列表（至少3点，推荐9点）</param>
        /// <param name="rmsThreshold">RMS误差合格阈值（mm，默认0.1mm）</param>
        /// <returns>标定精度报告</returns>
        /// <exception cref="ArgumentException">点数不足时抛出</exception>
        public CalibrationAccuracyReport Calibrate(
            List<CalibrationPoint> points,
            double rmsThreshold = 0.1)
        {
            if (points == null || points.Count < 3)
                throw new ArgumentException($"标定至少需要3个点，当前：{points?.Count ?? 0}个");

            _calibrationPoints = points.ToList();
            int n = points.Count;

            // ---- 最小二乘法求解仿射矩阵 ----
            // 对于 X 坐标：wx = m00*px + m01*py + m02
            // 构建方程组 A * x = b（x方向3个参数）
            // 对于 Y 坐标：wy = m10*px + m11*py + m12
            // 同样求解 y方向3个参数

            // 构建设计矩阵 A（n × 3）：每行 [px, py, 1]
            double[,] A = new double[n, 3];
            double[] bx = new double[n]; // x方向目标值
            double[] by = new double[n]; // y方向目标值

            for (int i = 0; i < n; i++)
            {
                A[i, 0] = points[i].PixelX;
                A[i, 1] = points[i].PixelY;
                A[i, 2] = 1.0;
                bx[i] = points[i].WorldX;
                by[i] = points[i].WorldY;
            }

            // 最小二乘解：x = (AᵀA)⁻¹Aᵀb
            double[] mx = LeastSquares3(A, bx, n); // [m00, m01, m02]
            double[] my = LeastSquares3(A, by, n); // [m10, m11, m12]

            // 构建 2×3 仿射矩阵
            var matrix = new double[2, 3];
            matrix[0, 0] = mx[0]; matrix[0, 1] = mx[1]; matrix[0, 2] = mx[2];
            matrix[1, 0] = my[0]; matrix[1, 1] = my[1]; matrix[1, 2] = my[2];

            // 计算像素尺寸（mm/pixel）：仿射矩阵的缩放分量
            double pixelSizeX = Math.Sqrt(mx[0] * mx[0] + mx[1] * mx[1]);
            double pixelSizeY = Math.Sqrt(my[0] * my[0] + my[1] * my[1]);

            // 评估标定精度
            var report = EvaluateResiduals(points, matrix);
            report.IsAcceptable = report.RmsError < rmsThreshold;

            // 保存标定结果
            _calibrationData = new CalibrationData
            {
                AffineMatrix = matrix,
                PixelSizeX = pixelSizeX,
                PixelSizeY = pixelSizeY,
                RmsError = report.RmsError,
                MaxError = report.MaxError,
                PointCount = n,
                CalibratedAt = DateTime.Now,
                IsValid = report.IsAcceptable
            };

            return report;
        }

        // ==================== 坐标转换 ====================

        /// <summary>
        /// 像素坐标 → 机械坐标转换。
        /// 使用仿射矩阵正向变换：[wx, wy] = M * [px, py, 1]ᵀ
        /// </summary>
        /// <param name="pixelX">像素X坐标</param>
        /// <param name="pixelY">像素Y坐标</param>
        /// <returns>(机械X, 机械Y) 单位mm</returns>
        /// <exception cref="InvalidOperationException">未完成标定时抛出</exception>
        public (double WorldX, double WorldY) PixelToWorld(double pixelX, double pixelY)
        {
            EnsureCalibrated();
            var m = _calibrationData!.AffineMatrix;
            double wx = m[0, 0] * pixelX + m[0, 1] * pixelY + m[0, 2];
            double wy = m[1, 0] * pixelX + m[1, 1] * pixelY + m[1, 2];
            return (wx, wy);
        }

        /// <summary>
        /// 机械坐标 → 像素坐标逆转换。
        /// 求解仿射矩阵的逆变换。
        /// 注意：仿射矩阵为 2×3，需通过解线性方程组实现逆转换。
        /// </summary>
        /// <param name="worldX">机械X坐标（mm）</param>
        /// <param name="worldY">机械Y坐标（mm）</param>
        /// <returns>(像素X, 像素Y)</returns>
        public (double PixelX, double PixelY) WorldToPixel(double worldX, double worldY)
        {
            EnsureCalibrated();
            var m = _calibrationData!.AffineMatrix;

            // 方程组：
            // m00*px + m01*py = wx - m02
            // m10*px + m11*py = wy - m12
            double a = m[0, 0], b = m[0, 1], e = worldX - m[0, 2];
            double c = m[1, 0], d = m[1, 1], f = worldY - m[1, 2];

            double det = a * d - b * c;
            if (Math.Abs(det) < 1e-10)
                throw new InvalidOperationException("仿射矩阵奇异，无法逆向转换");

            double px = (e * d - b * f) / det;
            double py = (a * f - e * c) / det;
            return (px, py);
        }

        // ==================== 精度评估 ====================

        /// <summary>
        /// 重新评估当前标定数据的精度（使用标定点计算残差）
        /// </summary>
        public CalibrationAccuracyReport EvaluateAccuracy()
        {
            EnsureCalibrated();
            return EvaluateResiduals(_calibrationPoints, _calibrationData!.AffineMatrix);
        }

        // ==================== 持久化 ====================

        /// <summary>
        /// 将标定数据序列化为 JSON 并保存到文件
        /// </summary>
        /// <param name="filePath">保存路径（如"calibration.json"）</param>
        public void SaveCalibration(string filePath)
        {
            EnsureCalibrated();
            var json = JsonSerializer.Serialize(_calibrationData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// 从 JSON 文件加载标定数据
        /// </summary>
        /// <param name="filePath">标定文件路径</param>
        /// <returns>是否加载成功</returns>
        public bool LoadCalibration(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                string json = File.ReadAllText(filePath);
                _calibrationData = JsonSerializer.Deserialize<CalibrationData>(json);
                return _calibrationData?.IsValid == true;
            }
            catch { return false; }
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 最小二乘法求解 Ax = b（3个未知数）
        /// 使用正规方程：(AᵀA)x = Aᵀb
        /// </summary>
        private static double[] LeastSquares3(double[,] A, double[] b, int n)
        {
            // 计算 AᵀA（3×3 对称矩阵）
            double[,] AtA = new double[3, 3];
            double[] Atb = new double[3];

            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    for (int k = 0; k < n; k++)
                        AtA[i, j] += A[k, i] * A[k, j];
                }
                for (int k = 0; k < n; k++)
                    Atb[i] += A[k, i] * b[k];
            }

            // 高斯消元求解 3×3 线性方程组
            return GaussElimination3x3(AtA, Atb);
        }

        /// <summary>
        /// 3×3 高斯消元法（带部分主元选取）
        /// </summary>
        private static double[] GaussElimination3x3(double[,] A, double[] b)
        {
            // 构建增广矩阵
            double[,] aug = new double[3, 4];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++) aug[i, j] = A[i, j];
                aug[i, 3] = b[i];
            }

            // 前向消元
            for (int col = 0; col < 3; col++)
            {
                // 选主元（最大绝对值）
                int pivotRow = col;
                for (int row = col + 1; row < 3; row++)
                    if (Math.Abs(aug[row, col]) > Math.Abs(aug[pivotRow, col]))
                        pivotRow = row;

                // 交换行
                if (pivotRow != col)
                    for (int k = 0; k <= 3; k++)
                        (aug[col, k], aug[pivotRow, k]) = (aug[pivotRow, k], aug[col, k]);

                if (Math.Abs(aug[col, col]) < 1e-12)
                    throw new InvalidOperationException("标定矩阵奇异，请检查标定点是否共线");

                // 消元
                for (int row = col + 1; row < 3; row++)
                {
                    double factor = aug[row, col] / aug[col, col];
                    for (int k = col; k <= 3; k++)
                        aug[row, k] -= factor * aug[col, k];
                }
            }

            // 回代
            double[] x = new double[3];
            for (int i = 2; i >= 0; i--)
            {
                x[i] = aug[i, 3];
                for (int j = i + 1; j < 3; j++)
                    x[i] -= aug[i, j] * x[j];
                x[i] /= aug[i, i];
            }
            return x;
        }

        /// <summary>
        /// 计算各标定点的转换误差（残差），并统计 RMS、最大值、平均值
        /// </summary>
        private static CalibrationAccuracyReport EvaluateResiduals(
            List<CalibrationPoint> points, double[,] matrix)
        {
            var residuals = new List<double>();
            double sumSq = 0;

            foreach (var pt in points)
            {
                // 用矩阵计算转换后的机械坐标
                double predictWx = matrix[0, 0] * pt.PixelX + matrix[0, 1] * pt.PixelY + matrix[0, 2];
                double predictWy = matrix[1, 0] * pt.PixelX + matrix[1, 1] * pt.PixelY + matrix[1, 2];

                // 计算欧式误差
                double ex = predictWx - pt.WorldX;
                double ey = predictWy - pt.WorldY;
                double error = Math.Sqrt(ex * ex + ey * ey);
                residuals.Add(error);
                sumSq += error * error;
            }

            double rms = Math.Sqrt(sumSq / points.Count);
            double maxErr = residuals.Max();
            double meanErr = residuals.Average();

            return new CalibrationAccuracyReport
            {
                RmsError = Math.Round(rms, 6),
                MaxError = Math.Round(maxErr, 6),
                MeanError = Math.Round(meanErr, 6),
                PointResiduals = residuals
            };
        }

        /// <summary>
        /// 检查是否已完成有效标定
        /// </summary>
        private void EnsureCalibrated()
        {
            if (!IsCalibrated)
                throw new InvalidOperationException("尚未完成标定，请先调用 Calibrate() 方法");
        }
    }
}
