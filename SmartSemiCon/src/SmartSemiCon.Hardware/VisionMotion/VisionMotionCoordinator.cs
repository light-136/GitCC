// ============================================================
// 文件：VisionMotionCoordinator.cs
// 用途：视觉与运动协同控制器
// 设计思路：
//   这是整个系统最核心的模块 — 视觉检测结果驱动运动控制。
//
//   关键概念：
//   1. 坐标转换 — 视觉像素坐标 → 机械运动坐标
//      通过标定矩阵（仿射变换）实现坐标映射
//   2. 飞拍 — 边运动边拍照，不停机采集
//      编码器触发模式：运动到指定位置时自动触发采集
//   3. 对位补偿 — 视觉检测偏差后，运动轴进行补偿运动
//
//   典型工作流程：
//   ① 运动到拍照位 → ② 触发相机采集 → ③ 视觉算法检测 →
//   ④ 坐标转换（像素→机械） → ⑤ 计算补偿量 → ⑥ 运动补偿
// ============================================================

using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;
using SmartSemiCon.Hardware.Motion.Axis;
using SmartSemiCon.Hardware.Vision;

namespace SmartSemiCon.Hardware.VisionMotion
{
    /// <summary>
    /// 坐标转换器 — 视觉像素坐标到机械坐标的转换。
    /// 使用仿射变换矩阵：[WorldX]   [a b c] [PixelX]
    ///                   [WorldY] = [d e f] [PixelY]
    ///                   [  1  ]   [0 0 1] [  1   ]
    /// </summary>
    public class CoordinateTransformer
    {
        // 仿射变换矩阵参数
        private double _a, _b, _c; // 第一行
        private double _d, _e, _f; // 第二行
        private bool _isCalibrated;

        /// <summary>是否已标定</summary>
        public bool IsCalibrated => _isCalibrated;

        /// <summary>
        /// 使用标定数据初始化变换矩阵。
        /// </summary>
        public void SetCalibration(CalibrationData calibData)
        {
            if (calibData.TransformMatrix.Length >= 6)
            {
                _a = calibData.TransformMatrix[0];
                _b = calibData.TransformMatrix[1];
                _c = calibData.TransformMatrix[2];
                _d = calibData.TransformMatrix[3];
                _e = calibData.TransformMatrix[4];
                _f = calibData.TransformMatrix[5];
                _isCalibrated = true;
            }
        }

        /// <summary>
        /// 设置简单标定参数 — 仅缩放和偏移（无旋转）。
        /// 适用于相机光轴与运动轴平行的简单场景。
        /// </summary>
        /// <param name="pixelToMm">像素到毫米的转换比例（mm/pixel）</param>
        /// <param name="offsetX">X方向偏移（mm）</param>
        /// <param name="offsetY">Y方向偏移（mm）</param>
        public void SetSimpleCalibration(double pixelToMm, double offsetX = 0, double offsetY = 0)
        {
            _a = pixelToMm; _b = 0; _c = offsetX;
            _d = 0; _e = pixelToMm; _f = offsetY;
            _isCalibrated = true;
        }

        /// <summary>
        /// 通过多点标定计算仿射变换矩阵（最小二乘法）。
        /// 至少需要3对标定点。
        /// </summary>
        /// <param name="points">标定点对列表</param>
        /// <returns>标定RMS误差（mm）</returns>
        public double CalibrateFromPoints(List<CalibrationPoint> points)
        {
            if (points.Count < 3) return -1;

            // 使用最小二乘法求解仿射变换矩阵
            // 构建方程组：WorldX = a*PixelX + b*PixelY + c
            //             WorldY = d*PixelX + e*PixelY + f
            int n = points.Count;
            double sumX = 0, sumY = 0, sumXX = 0, sumYY = 0, sumXY = 0;
            double sumWx = 0, sumWy = 0, sumXWx = 0, sumYWx = 0, sumXWy = 0, sumYWy = 0;

            foreach (var p in points)
            {
                sumX += p.PixelX; sumY += p.PixelY;
                sumXX += p.PixelX * p.PixelX;
                sumYY += p.PixelY * p.PixelY;
                sumXY += p.PixelX * p.PixelY;
                sumWx += p.WorldX; sumWy += p.WorldY;
                sumXWx += p.PixelX * p.WorldX;
                sumYWx += p.PixelY * p.WorldX;
                sumXWy += p.PixelX * p.WorldY;
                sumYWy += p.PixelY * p.WorldY;
            }

            // 求解 (简化实现：使用3点直接求解)
            if (n >= 3)
            {
                var p0 = points[0]; var p1 = points[1]; var p2 = points[2];

                var det = (p0.PixelX - p2.PixelX) * (p1.PixelY - p2.PixelY)
                        - (p1.PixelX - p2.PixelX) * (p0.PixelY - p2.PixelY);

                if (Math.Abs(det) < 1e-10) return -1;

                _a = ((p0.WorldX - p2.WorldX) * (p1.PixelY - p2.PixelY) - (p1.WorldX - p2.WorldX) * (p0.PixelY - p2.PixelY)) / det;
                _b = ((p1.WorldX - p2.WorldX) * (p0.PixelX - p2.PixelX) - (p0.WorldX - p2.WorldX) * (p1.PixelX - p2.PixelX)) / det;
                _c = p0.WorldX - _a * p0.PixelX - _b * p0.PixelY;

                _d = ((p0.WorldY - p2.WorldY) * (p1.PixelY - p2.PixelY) - (p1.WorldY - p2.WorldY) * (p0.PixelY - p2.PixelY)) / det;
                _e = ((p1.WorldY - p2.WorldY) * (p0.PixelX - p2.PixelX) - (p0.WorldY - p2.WorldY) * (p1.PixelX - p2.PixelX)) / det;
                _f = p0.WorldY - _d * p0.PixelX - _e * p0.PixelY;

                _isCalibrated = true;
            }

            // 计算RMS误差
            double sumError = 0;
            foreach (var p in points)
            {
                var (wx, wy) = Transform(p.PixelX, p.PixelY);
                sumError += Math.Pow(wx - p.WorldX, 2) + Math.Pow(wy - p.WorldY, 2);
            }
            return Math.Sqrt(sumError / n);
        }

        /// <summary>
        /// 像素坐标 → 机械坐标转换。
        /// </summary>
        public (double WorldX, double WorldY) Transform(double pixelX, double pixelY)
        {
            if (!_isCalibrated)
                return (pixelX * 0.01, pixelY * 0.01); // 未标定时使用默认比例

            var worldX = _a * pixelX + _b * pixelY + _c;
            var worldY = _d * pixelX + _e * pixelY + _f;
            return (worldX, worldY);
        }
    }

    /// <summary>
    /// 触发同步管理器 — 管理相机触发与运动的同步。
    /// </summary>
    public class TriggerManager
    {
        private readonly CameraManager _cameraManager;
        private readonly AxisManager _axisManager;

        /// <summary>触发间隔（编码器脉冲数，用于飞拍模式）</summary>
        public double TriggerInterval { get; set; } = 1.0; // mm

        public TriggerManager(CameraManager cameraManager, AxisManager axisManager)
        {
            _cameraManager = cameraManager;
            _axisManager = axisManager;
        }

        /// <summary>
        /// 软件触发采集 — 停机拍照模式。
        /// 运动到位 → 触发采集 → 等待图像。
        /// </summary>
        public async Task<byte[]?> SoftwareTriggerAsync(int cameraId, CancellationToken cancellationToken = default)
        {
            var camera = _cameraManager.GetCamera(cameraId);
            if (camera == null) return null;

            return await camera.CaptureAsync(cancellationToken);
        }

        /// <summary>
        /// 飞拍模式 — 边运动边拍照。
        /// 在运动轴到达每个触发位置时自动采集图像。
        /// </summary>
        /// <param name="cameraId">相机ID</param>
        /// <param name="axisId">运动轴ID</param>
        /// <param name="startPos">起始位置</param>
        /// <param name="endPos">终点位置</param>
        /// <param name="velocity">运动速度</param>
        /// <returns>采集的图像数据列表</returns>
        public async Task<List<byte[]>> FlyCaptureModeAsync(int cameraId, int axisId,
            double startPos, double endPos, double velocity, CancellationToken cancellationToken = default)
        {
            var images = new List<byte[]>();
            var camera = _cameraManager.GetCamera(cameraId);
            var axis = _axisManager.GetAxis(axisId);
            if (camera == null || axis == null) return images;

            // 先移动到起始位置
            await axis.MoveAbsoluteAsync(startPos, velocity, 500, 500, cancellationToken);

            // 计算触发位置列表
            var direction = endPos > startPos ? 1.0 : -1.0;
            var totalDistance = Math.Abs(endPos - startPos);
            var triggerCount = (int)(totalDistance / TriggerInterval);

            // 启动运动
            var motionTask = axis.MoveAbsoluteAsync(endPos, velocity, 500, 500, cancellationToken);

            // 在运动过程中按位置触发采集
            for (int i = 0; i < triggerCount && !cancellationToken.IsCancellationRequested; i++)
            {
                var triggerPos = startPos + direction * TriggerInterval * (i + 1);

                // 等待轴到达触发位置
                while (Math.Abs(axis.Status.Position - triggerPos) > 0.1 && axis.Status.IsMoving)
                {
                    await Task.Delay(1, cancellationToken);
                }

                // 触发采集
                var image = await camera.CaptureAsync(cancellationToken);
                if (image != null) images.Add(image);
            }

            await motionTask;
            return images;
        }
    }

    /// <summary>
    /// 视觉-运动协同控制器 — 核心中的核心。
    /// 将视觉检测结果转换为运动补偿动作。
    /// </summary>
    public class VisionMotionCoordinator
    {
        private readonly AxisManager _axisManager;
        private readonly CameraManager _cameraManager;
        private readonly IVisionEngine _visionEngine;
        private readonly CoordinateTransformer _transformer;
        private readonly TriggerManager _triggerManager;

        public VisionMotionCoordinator(
            AxisManager axisManager,
            CameraManager cameraManager,
            IVisionEngine visionEngine)
        {
            _axisManager = axisManager;
            _cameraManager = cameraManager;
            _visionEngine = visionEngine;
            _transformer = new CoordinateTransformer();
            _triggerManager = new TriggerManager(cameraManager, axisManager);
        }

        /// <summary>坐标转换器</summary>
        public CoordinateTransformer Transformer => _transformer;

        /// <summary>触发管理器</summary>
        public TriggerManager TriggerMgr => _triggerManager;

        /// <summary>
        /// 视觉对位 — 完整的视觉定位+运动补偿流程。
        ///
        /// 流程：
        /// 1. 触发相机采集图像
        /// 2. 视觉算法检测目标位置（像素坐标）
        /// 3. 坐标转换（像素→机械）
        /// 4. 计算偏差和补偿量
        /// 5. 运动轴执行补偿运动
        /// </summary>
        /// <param name="cameraId">相机ID</param>
        /// <param name="axisXId">X轴ID</param>
        /// <param name="axisYId">Y轴ID</param>
        /// <param name="targetWorldX">目标机械坐标X</param>
        /// <param name="targetWorldY">目标机械坐标Y</param>
        /// <returns>对位结果（是否成功 + 最终偏差）</returns>
        public async Task<(bool Success, double ErrorX, double ErrorY)> AlignAsync(
            int cameraId, int axisXId, int axisYId,
            double targetWorldX, double targetWorldY,
            CancellationToken cancellationToken = default)
        {
            // 步骤1：采集图像
            var camera = _cameraManager.GetCamera(cameraId);
            if (camera == null) return (false, 0, 0);

            var imageData = await camera.CaptureAsync(cancellationToken);
            if (imageData == null) return (false, 0, 0);

            // 步骤2：视觉检测（Mark点定位）
            var result = await _visionEngine.FindMarkAsync(
                imageData, camera.Config.ImageWidth, camera.Config.ImageHeight);

            if (!result.IsSuccess) return (false, 0, 0);

            // 步骤3：坐标转换（像素→机械）
            var (worldX, worldY) = _transformer.Transform(result.PixelX, result.PixelY);
            result.WorldX = worldX;
            result.WorldY = worldY;

            // 步骤4：计算补偿量
            var compensateX = targetWorldX - worldX;
            var compensateY = targetWorldY - worldY;

            // 步骤5：运动补偿
            var axisX = _axisManager.GetAxis(axisXId);
            var axisY = _axisManager.GetAxis(axisYId);
            if (axisX == null || axisY == null) return (false, compensateX, compensateY);

            // 执行相对运动补偿
            var moveXTask = axisX.MoveRelativeAsync(compensateX, 10, 200, 200, cancellationToken);
            var moveYTask = axisY.MoveRelativeAsync(compensateY, 10, 200, 200, cancellationToken);

            await Task.WhenAll(moveXTask, moveYTask);

            return (moveXTask.Result && moveYTask.Result, compensateX, compensateY);
        }

        /// <summary>
        /// 九点标定 — 通过运动到9个已知位置，采集图像建立坐标映射。
        /// 标定流程：
        /// 1. 运动到标定点位 → 2. 拍照 → 3. 视觉检测Mark点像素坐标
        /// 4. 记录（像素坐标, 机械坐标）点对 → 5. 求解仿射变换矩阵
        /// </summary>
        public async Task<CalibrationData?> NinePointCalibrationAsync(
            int cameraId, int axisXId, int axisYId,
            double centerX, double centerY, double stepSize,
            CancellationToken cancellationToken = default)
        {
            var camera = _cameraManager.GetCamera(cameraId);
            var axisX = _axisManager.GetAxis(axisXId);
            var axisY = _axisManager.GetAxis(axisYId);
            if (camera == null || axisX == null || axisY == null) return null;

            var calibPoints = new List<CalibrationPoint>();

            // 9个标定点（3x3网格）
            for (int row = -1; row <= 1; row++)
            {
                for (int col = -1; col <= 1; col++)
                {
                    if (cancellationToken.IsCancellationRequested) return null;

                    // 运动到标定位置
                    var targetX = centerX + col * stepSize;
                    var targetY = centerY + row * stepSize;

                    await axisX.MoveAbsoluteAsync(targetX, 50, 200, 200, cancellationToken);
                    await axisY.MoveAbsoluteAsync(targetY, 50, 200, 200, cancellationToken);

                    // 等待到位
                    await Task.Delay(200, cancellationToken);

                    // 拍照
                    var image = await camera.CaptureAsync(cancellationToken);
                    if (image == null) continue;

                    // 视觉检测
                    var result = await _visionEngine.FindMarkAsync(
                        image, camera.Config.ImageWidth, camera.Config.ImageHeight);

                    if (result.IsSuccess)
                    {
                        calibPoints.Add(new CalibrationPoint
                        {
                            PixelX = result.PixelX,
                            PixelY = result.PixelY,
                            WorldX = targetX,
                            WorldY = targetY
                        });
                    }
                }
            }

            if (calibPoints.Count < 3) return null;

            // 计算标定矩阵
            var rmsError = _transformer.CalibrateFromPoints(calibPoints);

            return new CalibrationData
            {
                Name = $"Calib_{DateTime.Now:yyyyMMdd_HHmmss}",
                CameraId = cameraId,
                Points = calibPoints,
                RmsError = rmsError,
                CalibratedAt = DateTime.Now
            };
        }
    }
}
