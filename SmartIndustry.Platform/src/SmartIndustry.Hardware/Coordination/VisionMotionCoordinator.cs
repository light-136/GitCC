// ============================================================
// 文件：VisionMotionCoordinator.cs
// 层级：硬件抽象层（Hardware Layer）> Coordination
// 职责：视觉-运动协同控制器（核心模块）。
//       编排"拍照→定位→补偿→移动"的完整工作流，
//       是视觉引导运动（Visual Guided Motion）的核心实现。
//
// 功能概述：
//   1. 拍照-定位-移动 完整流程（Capture-Locate-Move）
//   2. 坐标转换（调用 CalibrationService 的仿射矩阵）
//   3. 对位补偿计算（计算理论位置与实测位置的偏差，叠加到运动指令）
//   4. 飞拍模式（边运动边采集，编码器位置触发采集信号）
//   5. Mark点定位流程（对准两个Mark点确定工件姿态）
//   6. 多工位协同（顺序或并行执行多个工位的视觉检测）
//   7. 精度统计（记录历史补偿量和定位误差）
//
// 设计思路：
//   协调者模式（Coordinator/Orchestrator）：
//     不直接实现视觉或运动功能，而是调用 IVisionEngine 和 AxisController，
//     通过预定义的步骤序列实现复杂的多步骤自动化流程。
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using System.Collections.Concurrent;
using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Models;
using SmartIndustry.Hardware.Motion.Drivers;
using SmartIndustry.Hardware.Vision.Engines;
using SmartIndustry.Hardware.Vision.Calibration;
using SmartIndustry.Hardware;

namespace SmartIndustry.Hardware.Coordination
{
    /// <summary>
    /// 对位补偿结果 — 描述一次视觉引导对位的补偿量和精度。
    /// </summary>
    public class AlignmentResult
    {
        /// <summary>视觉检测到的像素偏移X（像素）</summary>
        public double PixelOffsetX { get; set; }

        /// <summary>视觉检测到的像素偏移Y（像素）</summary>
        public double PixelOffsetY { get; set; }

        /// <summary>转换为机械坐标后的偏移X（mm）</summary>
        public double WorldOffsetX { get; set; }

        /// <summary>转换为机械坐标后的偏移Y（mm）</summary>
        public double WorldOffsetY { get; set; }

        /// <summary>旋转补偿角度（度）</summary>
        public double RotationDeg { get; set; }

        /// <summary>视觉匹配得分</summary>
        public double MatchScore { get; set; }

        /// <summary>是否需要补偿（偏差超过容差阈值）</summary>
        public bool NeedsCorrection { get; set; }

        /// <summary>是否在允许的最大补偿范围内</summary>
        public bool IsWithinRange { get; set; }

        /// <summary>定位时间戳</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 工位定义 — 描述一个视觉检测/作业工位。
    /// </summary>
    public class WorkStation
    {
        /// <summary>工位编号</summary>
        public int StationId { get; set; }

        /// <summary>工位名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>理论停靠位置X（mm）</summary>
        public double NominalX { get; set; }

        /// <summary>理论停靠位置Y（mm）</summary>
        public double NominalY { get; set; }

        /// <summary>最大允许偏差（mm，超过此值报警）</summary>
        public double MaxOffset { get; set; } = 5.0;

        /// <summary>最小对位精度要求（mm，补偿后仍需满足）</summary>
        public double RequiredAccuracy { get; set; } = 0.1;

        /// <summary>对应的轴X标识</summary>
        public string AxisXId { get; set; } = "X";

        /// <summary>对应的轴Y标识</summary>
        public string AxisYId { get; set; } = "Y";

        /// <summary>关联的视觉引擎ID</summary>
        public string VisionEngineId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 多工位协同执行结果
    /// </summary>
    public class MultiStationResult
    {
        /// <summary>执行的工位数量</summary>
        public int TotalStations { get; set; }

        /// <summary>成功完成的工位数</summary>
        public int SuccessCount { get; set; }

        /// <summary>失败的工位数</summary>
        public int FailedCount { get; set; }

        /// <summary>总执行时间</summary>
        public TimeSpan TotalDuration { get; set; }

        /// <summary>各工位的对位结果</summary>
        public Dictionary<int, AlignmentResult> StationResults { get; set; } = new();
    }

    /// <summary>
    /// 精度统计数据 — 历史对位精度的统计分析。
    /// </summary>
    public class AccuracyStatistics
    {
        /// <summary>统计样本数</summary>
        public int SampleCount { get; set; }

        /// <summary>X方向补偿均值（mm）</summary>
        public double MeanOffsetX { get; set; }

        /// <summary>Y方向补偿均值（mm）</summary>
        public double MeanOffsetY { get; set; }

        /// <summary>X方向补偿标准差（mm）</summary>
        public double StdDevX { get; set; }

        /// <summary>Y方向补偿标准差（mm）</summary>
        public double StdDevY { get; set; }

        /// <summary>最大单次补偿量（mm）</summary>
        public double MaxOffset { get; set; }

        /// <summary>超出容差次数</summary>
        public int OutOfRangeCount { get; set; }

        /// <summary>统计时间范围</summary>
        public DateTime StatisticsFrom { get; set; }
    }

    /// <summary>
    /// 视觉-运动协同控制器。
    /// 编排视觉引导运动（VGM）的完整工作流。
    ///
    /// 工作流程（标准对位补偿）：
    ///   1. 轴移动到预拍照位置
    ///   2. 触发视觉采集和模板匹配
    ///   3. 将匹配结果像素坐标转换为机械坐标（CalibrationService）
    ///   4. 计算与理论位置的偏差
    ///   5. 叠加偏差补偿，下发最终运动指令
    ///   6. 等待运动完成，记录精度统计
    /// </summary>
    public class VisionMotionCoordinator : IDisposable
    {
        // ==================== 私有字段 ====================

        /// <summary>视觉引擎映射（Key=EngineId）</summary>
        private readonly Dictionary<string, IVisionEngine> _visionEngines;

        /// <summary>轴控制器映射（Key=AxisId）</summary>
        private readonly Dictionary<string, AxisController> _axisControllers;

        /// <summary>标定服务（像素↔机械坐标转换）</summary>
        private readonly CalibrationService _calibrationService;

        /// <summary>事件总线</summary>
        private readonly IEventBus _eventBus;

        /// <summary>历史补偿记录（用于统计分析，最多保留1000条）</summary>
        private readonly ConcurrentQueue<AlignmentResult> _historyRecords = new();

        /// <summary>最大历史记录数</summary>
        private const int MaxHistorySize = 1000;

        /// <summary>对位精度容差（mm，偏差小于此值不补偿）</summary>
        public double AlignmentTolerance { get; set; } = 0.05;

        /// <summary>最大补偿范围（mm，超过此值视为异常，不执行补偿）</summary>
        public double MaxCorrectionRange { get; set; } = 10.0;

        // ==================== 事件 ====================

        /// <summary>对位补偿完成时触发</summary>
        public event EventHandler<AlignmentResult>? AlignmentCompleted;

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造视觉-运动协同控制器
        /// </summary>
        public VisionMotionCoordinator(
            Dictionary<string, IVisionEngine> visionEngines,
            Dictionary<string, AxisController> axisControllers,
            CalibrationService calibrationService,
            IEventBus eventBus)
        {
            _visionEngines = visionEngines ?? throw new ArgumentNullException(nameof(visionEngines));
            _axisControllers = axisControllers ?? throw new ArgumentNullException(nameof(axisControllers));
            _calibrationService = calibrationService ?? throw new ArgumentNullException(nameof(calibrationService));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        // ==================== 核心流程 ====================

        /// <summary>
        /// 执行完整的拍照-定位-移动流程（Capture-Locate-Move）。
        /// 步骤：
        ///   1. 轴移动到预拍照位置（capturePositionX, capturePositionY）
        ///   2. 触发视觉采集和模板匹配
        ///   3. 计算对位补偿量
        ///   4. 执行补偿运动（移动到理论位置+补偿偏移）
        /// </summary>
        /// <param name="engineId">视觉引擎ID</param>
        /// <param name="axisXId">X轴ID</param>
        /// <param name="axisYId">Y轴ID</param>
        /// <param name="capturePositionX">拍照位置X（mm）</param>
        /// <param name="capturePositionY">拍照位置Y（mm）</param>
        /// <param name="targetPositionX">理论目标位置X（mm）</param>
        /// <param name="targetPositionY">理论目标位置Y（mm）</param>
        /// <param name="templateId">匹配模板ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task<AlignmentResult> CaptureLockMoveAsync(
            string engineId,
            string axisXId, string axisYId,
            double capturePositionX, double capturePositionY,
            double targetPositionX, double targetPositionY,
            string templateId = "default",
            CancellationToken cancellationToken = default)
        {
            // ---- Step1: 移动到拍照位置 ----
            await MoveAxesAsync(axisXId, axisYId, capturePositionX, capturePositionY, cancellationToken);

            // ---- Step2: 触发视觉采集 ----
            var engine = GetEngine(engineId);
            var image = await engine.CaptureAsync(engineId, cancellationToken);

            // ---- Step3: 模板匹配（通过 ExecuteTaskAsync 统一入口） ----
            var visionTask = new VisionTask
            {
                TaskName = $"对位匹配_{templateId}",
                TaskType = VisionTaskType.PatternMatch,
                IsTemplate = false,
                CameraId = engineId,
                Parameters = $"{{\"TemplatePath\": \"{templateId}\"}}"
            };
            var taskResult = await engine.ExecuteTaskAsync(visionTask, image, cancellationToken);
            var match = taskResult.Data as TemplateMatchResult;
            if (!taskResult.IsSuccess || match?.Found != true)
            {
                return new AlignmentResult
                {
                    MatchScore = 0,
                    NeedsCorrection = false,
                    IsWithinRange = false
                };
            }

            // ---- Step4: 像素坐标→机械坐标 ----
            AlignmentResult alignment;
            if (_calibrationService.IsCalibrated)
            {
                var (worldX, worldY) = _calibrationService.PixelToWorld(match.X, match.Y);
                double offsetX = worldX - targetPositionX;
                double offsetY = worldY - targetPositionY;
                double totalOffset = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);

                alignment = new AlignmentResult
                {
                    PixelOffsetX = match.X - (image.Width / 2.0),
                    PixelOffsetY = match.Y - (image.Height / 2.0),
                    WorldOffsetX = offsetX,
                    WorldOffsetY = offsetY,
                    RotationDeg = match.Angle,
                    MatchScore = match.Score,
                    NeedsCorrection = totalOffset > AlignmentTolerance,
                    IsWithinRange = totalOffset <= MaxCorrectionRange
                };
            }
            else
            {
                // 未标定时，使用简单比例转换（估算）
                alignment = new AlignmentResult
                {
                    PixelOffsetX = match.X - (image.Width / 2.0),
                    PixelOffsetY = match.Y - (image.Height / 2.0),
                    WorldOffsetX = 0,
                    WorldOffsetY = 0,
                    MatchScore = match.Score,
                    NeedsCorrection = false,
                    IsWithinRange = true
                };
            }

            // ---- Step5: 执行补偿运动 ----
            if (alignment.NeedsCorrection && alignment.IsWithinRange)
            {
                double correctedX = targetPositionX - alignment.WorldOffsetX;
                double correctedY = targetPositionY - alignment.WorldOffsetY;
                await MoveAxesAsync(axisXId, axisYId, correctedX, correctedY, cancellationToken);
            }
            else
            {
                await MoveAxesAsync(axisXId, axisYId, targetPositionX, targetPositionY, cancellationToken);
            }

            // ---- 记录历史统计 ----
            AddToHistory(alignment);
            AlignmentCompleted?.Invoke(this, alignment);

            // 发布协同事件
            _ = _eventBus.PublishAsync(new HardwareVisionMotionAlignedEvent(
                engineId, alignment.WorldOffsetX, alignment.WorldOffsetY, alignment.MatchScore));

            return alignment;
        }

        // ==================== Mark点定位 ====================

        /// <summary>
        /// Mark点定位流程 — 通过两个Mark点确定工件姿态（位置+旋转角度）。
        /// 步骤：
        ///   1. 移动到Mark1位置，视觉识别Mark1
        ///   2. 移动到Mark2位置，视觉识别Mark2
        ///   3. 根据两点的实测位置计算旋转角度和平移偏移
        ///   4. 返回工件姿态参数
        /// </summary>
        public async Task<(double OffsetX, double OffsetY, double RotationDeg)> LocateByMarkPointsAsync(
            string engineId,
            string axisXId, string axisYId,
            (double X, double Y) mark1Nominal, // Mark1理论位置
            (double X, double Y) mark2Nominal, // Mark2理论位置
            string templateId = "mark",
            CancellationToken cancellationToken = default)
        {
            // 识别 Mark1
            await MoveAxesAsync(axisXId, axisYId, mark1Nominal.X, mark1Nominal.Y, cancellationToken);
            var engine = GetEngine(engineId);
            var markTask = new VisionTask
            {
                TaskName = $"Mark点定位_{templateId}",
                TaskType = VisionTaskType.PatternMatch,
                IsTemplate = false,
                CameraId = engineId,
                Parameters = $"{{\"TemplatePath\": \"{templateId}\"}}"
            };

            var img1 = await engine.CaptureAsync(engineId, cancellationToken);
            var result1 = await engine.ExecuteTaskAsync(markTask, img1, cancellationToken);
            var match1Data = result1.Data as TemplateMatchResult;

            // 识别 Mark2
            await MoveAxesAsync(axisXId, axisYId, mark2Nominal.X, mark2Nominal.Y, cancellationToken);
            var img2 = await engine.CaptureAsync(engineId, cancellationToken);
            var result2 = await engine.ExecuteTaskAsync(markTask, img2, cancellationToken);
            var match2Data = result2.Data as TemplateMatchResult;

            // 如果任一 Mark 未识别，返回零补偿
            if (match1Data?.Found != true || match2Data?.Found != true)
                return (0, 0, 0);

            // 坐标转换
            (double w1x, double w1y) = _calibrationService.IsCalibrated
                ? _calibrationService.PixelToWorld(match1Data!.X, match1Data!.Y)
                : (mark1Nominal.X, mark1Nominal.Y);

            (double w2x, double w2y) = _calibrationService.IsCalibrated
                ? _calibrationService.PixelToWorld(match2Data!.X, match2Data!.Y)
                : (mark2Nominal.X, mark2Nominal.Y);

            // 计算旋转角度（实测Mark连线与理论连线的夹角）
            double nominalAngle = Math.Atan2(mark2Nominal.Y - mark1Nominal.Y,
                                              mark2Nominal.X - mark1Nominal.X);
            double actualAngle = Math.Atan2(w2y - w1y, w2x - w1x);
            double rotationDeg = (actualAngle - nominalAngle) * 180.0 / Math.PI;

            // 平移偏移（取两个Mark点偏移的平均）
            double offsetX = ((w1x - mark1Nominal.X) + (w2x - mark2Nominal.X)) / 2.0;
            double offsetY = ((w1y - mark1Nominal.Y) + (w2y - mark2Nominal.Y)) / 2.0;

            return (offsetX, offsetY, rotationDeg);
        }

        // ==================== 飞拍模式 ====================

        /// <summary>
        /// 飞拍模式 — 轴在运动过程中，当到达指定编码器位置时触发视觉采集。
        /// 简化版：启动轴运动后，每隔指定编码器间隔轮询位置，到达触发位置时采集。
        /// </summary>
        /// <param name="engineId">视觉引擎ID</param>
        /// <param name="axisId">运动轴ID</param>
        /// <param name="startPosition">起始位置（mm）</param>
        /// <param name="endPosition">结束位置（mm）</param>
        /// <param name="triggerPositions">触发采集的编码器位置列表（mm）</param>
        /// <param name="velocity">运动速度（mm/s）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>各触发位置的采集图像列表</returns>
        public async Task<List<(double TriggerPosition, ImageData Image)>> FlyShootAsync(
            string engineId,
            string axisId,
            double startPosition,
            double endPosition,
            List<double> triggerPositions,
            double velocity = 100.0,
            CancellationToken cancellationToken = default)
        {
            var results = new List<(double, ImageData)>();
            var engine = GetEngine(engineId);
            var axis = GetAxis(axisId);

            // 移动到起始位置
            await axis.MoveAbsoluteAsync(startPosition, cancellationToken: cancellationToken);

            // 排序触发位置
            var sortedTriggers = triggerPositions.OrderBy(p => p).ToList();
            int triggerIndex = 0;

            // 启动运动（异步，不等待）
            var moveTask = axis.MoveAbsoluteAsync(endPosition, velocity, cancellationToken: cancellationToken);

            // 轮询位置触发采集（10ms轮询）
            while (!moveTask.IsCompleted && triggerIndex < sortedTriggers.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double currentPos = axis.GetActualPosition();

                if (currentPos >= sortedTriggers[triggerIndex])
                {
                    // 触发采集
                    var image = await engine.CaptureAsync(engineId, cancellationToken);
                    results.Add((sortedTriggers[triggerIndex], image));
                    triggerIndex++;
                }

                await Task.Delay(10, cancellationToken);
            }

            // 等待运动完成
            await moveTask;
            return results;
        }

        // ==================== 多工位协同 ====================

        /// <summary>
        /// 顺序执行多个工位的视觉对位（每个工位完成后再执行下一个）
        /// </summary>
        public async Task<MultiStationResult> ExecuteStationsSequentialAsync(
            List<WorkStation> stations,
            string templateId = "default",
            CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var stationResults = new Dictionary<int, AlignmentResult>();
            int successCount = 0;

            foreach (var station in stations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await CaptureLockMoveAsync(
                        station.VisionEngineId,
                        station.AxisXId, station.AxisYId,
                        station.NominalX, station.NominalY,
                        station.NominalX, station.NominalY,
                        templateId, cancellationToken);

                    stationResults[station.StationId] = result;
                    if (result.IsWithinRange) successCount++;
                }
                catch (Exception ex)
                {
                    stationResults[station.StationId] = new AlignmentResult
                    {
                        IsWithinRange = false,
                        NeedsCorrection = false,
                        MatchScore = 0
                    };
                }
            }

            sw.Stop();
            return new MultiStationResult
            {
                TotalStations = stations.Count,
                SuccessCount = successCount,
                FailedCount = stations.Count - successCount,
                TotalDuration = sw.Elapsed,
                StationResults = stationResults
            };
        }

        /// <summary>
        /// 并行执行多个工位的视觉对位（所有工位同时开始，等待全部完成）
        /// 注意：并行执行要求各工位使用不同的轴（防止轴冲突）
        /// </summary>
        public async Task<MultiStationResult> ExecuteStationsParallelAsync(
            List<WorkStation> stations,
            string templateId = "default",
            CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tasks = stations.Select(async station =>
            {
                try
                {
                    var result = await CaptureLockMoveAsync(
                        station.VisionEngineId,
                        station.AxisXId, station.AxisYId,
                        station.NominalX, station.NominalY,
                        station.NominalX, station.NominalY,
                        templateId, cancellationToken);
                    return (station.StationId, result);
                }
                catch
                {
                    return (station.StationId, new AlignmentResult { IsWithinRange = false });
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            sw.Stop();

            var stationResults = results.ToDictionary(r => r.Item1, r => r.Item2);
            int successCount = stationResults.Values.Count(r => r.IsWithinRange);

            return new MultiStationResult
            {
                TotalStations = stations.Count,
                SuccessCount = successCount,
                FailedCount = stations.Count - successCount,
                TotalDuration = sw.Elapsed,
                StationResults = stationResults
            };
        }

        // ==================== 精度统计 ====================

        /// <summary>
        /// 获取历史对位精度统计
        /// </summary>
        public AccuracyStatistics GetAccuracyStatistics()
        {
            var records = _historyRecords.ToArray();
            if (records.Length == 0)
                return new AccuracyStatistics { SampleCount = 0 };

            double meanX = records.Average(r => r.WorldOffsetX);
            double meanY = records.Average(r => r.WorldOffsetY);
            double stdX = Math.Sqrt(records.Average(r => Math.Pow(r.WorldOffsetX - meanX, 2)));
            double stdY = Math.Sqrt(records.Average(r => Math.Pow(r.WorldOffsetY - meanY, 2)));
            double maxOff = records.Max(r => Math.Sqrt(r.WorldOffsetX * r.WorldOffsetX + r.WorldOffsetY * r.WorldOffsetY));
            int outCount = records.Count(r => !r.IsWithinRange);

            return new AccuracyStatistics
            {
                SampleCount = records.Length,
                MeanOffsetX = Math.Round(meanX, 6),
                MeanOffsetY = Math.Round(meanY, 6),
                StdDevX = Math.Round(stdX, 6),
                StdDevY = Math.Round(stdY, 6),
                MaxOffset = Math.Round(maxOff, 6),
                OutOfRangeCount = outCount,
                StatisticsFrom = records.Min(r => r.Timestamp)
            };
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 并发移动X和Y轴到目标位置
        /// </summary>
        private async Task MoveAxesAsync(string axisXId, string axisYId,
            double targetX, double targetY, CancellationToken ct)
        {
            var axisX = GetAxis(axisXId);
            var axisY = GetAxis(axisYId);
            await Task.WhenAll(
                axisX.MoveAbsoluteAsync(targetX, cancellationToken: ct),
                axisY.MoveAbsoluteAsync(targetY, cancellationToken: ct));
        }

        private IVisionEngine GetEngine(string engineId)
        {
            if (!_visionEngines.TryGetValue(engineId, out var engine))
                throw new KeyNotFoundException($"视觉引擎[{engineId}]未注册");
            return engine;
        }

        private AxisController GetAxis(string axisId)
        {
            if (!_axisControllers.TryGetValue(axisId, out var axis))
                throw new KeyNotFoundException($"轴[{axisId}]未注册");
            return axis;
        }

        private void AddToHistory(AlignmentResult result)
        {
            _historyRecords.Enqueue(result);
            // 超过最大数量时移除最旧记录
            while (_historyRecords.Count > MaxHistorySize)
                _historyRecords.TryDequeue(out _);
        }

        // ==================== IDisposable ====================

        /// <inheritdoc/>
        public void Dispose() { }
    }
}
