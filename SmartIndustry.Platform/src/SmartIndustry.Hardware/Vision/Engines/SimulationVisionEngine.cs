// ============================================================
// 文件：SimulationVisionEngine.cs
// 层级：硬件抽象层（Hardware Layer）> Vision > Engines
// 职责：模拟视觉引擎，实现 IVisionEngine 接口。
//       用于开发/测试阶段无真实相机时的功能验证。
//       生产环境替换为 HalconVisionEngine、OpenCVVisionEngine 等。
//
// 模拟行为说明：
//   - 图像采集：生成带渐变+噪声的随机测试图像（640×480 灰度）
//   - 模板匹配：随机得分 85~100，位置±5像素随机偏移
//   - Blob分析：随机生成 1~5 个 Blob，面积和位置随机
//   - 尺寸测量：基准值±随机偏差（±0.1mm），偶尔超出公差
//   - OCR识别：返回预设字符串列表中的随机一个
//   - 缺陷检测：95%概率OK，4%概率NG，1%概率Uncertain
//   - 执行延时：50~200ms随机延时（模拟真实算法耗时）
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Models;
using SmartIndustry.Domain.ValueObjects;

namespace SmartIndustry.Hardware.Vision.Engines
{
    /// <summary>
    /// 模拟视觉引擎（30帧仿真，纯软件，无真实相机依赖）。
    /// 所有视觉方法均返回符合格式要求的随机仿真数据，
    /// 并通过 IEventBus 发布视觉结果事件。
    /// </summary>
    public class SimulationVisionEngine : IVisionEngine
    {
        // ==================== 私有字段 ====================

        /// <summary>引擎唯一ID</summary>
        private readonly string _engineId;

        /// <summary>事件总线（发布视觉结果事件）</summary>
        private readonly IEventBus _eventBus;

        /// <summary>随机数生成器（线程安全：锁保护）</summary>
        private readonly Random _random = new();

        /// <summary>随机数访问锁</summary>
        private readonly object _randomLock = new();

        /// <summary>模拟图像宽度（像素）</summary>
        private int _imageWidth = 640;

        /// <summary>模拟图像高度（像素）</summary>
        private int _imageHeight = 480;

        /// <summary>是否已初始化</summary>
        private volatile bool _isReady;

        /// <summary>是否已释放</summary>
        private bool _disposed;

        /// <summary>已加载的模型（Key=模型ID, Value=路径）</summary>
        private readonly Dictionary<string, string> _loadedModels = new();

        /// <summary>各任务的ROI设置（Key=任务ID）</summary>
        private readonly Dictionary<Guid, VisionRegion> _roiMap = new();

        // OCR 预设字符串池（模拟识别结果）
        private static readonly string[] OcrStringPool =
        {
            "SN20260525001", "SN20260525002", "LOT-A001",
            "PASS", "2026-05-25", "QR-SCAN-OK", "P/N:12345"
        };

        // 模拟相机列表
        private static readonly string[] SimCameraList = { "SimCam0", "SimCam1" };

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造模拟视觉引擎
        /// </summary>
        /// <param name="engineId">引擎标识（如"SimCam0"）</param>
        /// <param name="eventBus">事件总线</param>
        /// <param name="imageWidth">模拟图像宽度（像素）</param>
        /// <param name="imageHeight">模拟图像高度（像素）</param>
        public SimulationVisionEngine(
            string engineId,
            IEventBus eventBus,
            int imageWidth = 640,
            int imageHeight = 480)
        {
            _engineId = engineId;
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _imageWidth = imageWidth;
            _imageHeight = imageHeight;
        }

        // ==================== IVisionEngine 属性实现 ====================

        /// <inheritdoc/>
        public string EngineId => _engineId;

        /// <inheritdoc/>
        public bool IsReady => _isReady;

        // ==================== 生命周期 ====================

        /// <summary>
        /// 初始化模拟视觉引擎（模拟200ms初始化延时）
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(200, cancellationToken);
            _isReady = true;
        }

        /// <summary>
        /// 关闭模拟视觉引擎
        /// </summary>
        public Task ShutdownAsync()
        {
            _isReady = false;
            return Task.CompletedTask;
        }

        // ==================== 图像采集 ====================

        /// <summary>
        /// 模拟图像采集：生成带渐变+随机噪声的灰度图像。
        /// </summary>
        public async Task<ImageData> CaptureAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            EnsureReady();
            await SimulateDelay(30, 80, cancellationToken);

            var image = ImageData.Create(_imageWidth, _imageHeight, 1);

            lock (_randomLock)
            {
                for (int y = 0; y < _imageHeight; y++)
                {
                    for (int x = 0; x < _imageWidth; x++)
                    {
                        double baseVal = 100 + 80.0 * x / _imageWidth + 40.0 * y / _imageHeight;
                        double noise = (_random.NextDouble() + _random.NextDouble() - 1.0) * 8;
                        int pixVal = Math.Clamp((int)(baseVal + noise), 0, 255);
                        image.Pixels[y * _imageWidth + x] = (byte)pixVal;
                    }
                }
            }

            image.SourceId = cameraId;
            return image;
        }

        // ==================== 视觉任务统一执行入口 ====================

        /// <summary>
        /// 执行视觉任务（按 TaskType 分发到对应模拟算法）
        /// </summary>
        public async Task<VisionResult<object>> ExecuteTaskAsync(
            VisionTask task,
            ImageData? image = null,
            CancellationToken cancellationToken = default)
        {
            EnsureReady();

            // 如果没有传入图像，自动采集
            image ??= await CaptureAsync(task.CameraId ?? _engineId, cancellationToken);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await SimulateDelay(50, 200, cancellationToken);

            object resultData;
            bool isPass;
            double score;

            lock (_randomLock)
            {
                switch (task.TaskType)
                {
                    case Domain.Enums.VisionTaskType.PatternMatch:
                        bool found = _random.NextDouble() > 0.05;
                        var matchResult = new TemplateMatchResult
                        {
                            Found = found,
                            Score = found ? 85.0 + _random.NextDouble() * 15.0 : 0,
                            X = found ? _imageWidth / 2.0 + (_random.NextDouble() - 0.5) * 20 : 0,
                            Y = found ? _imageHeight / 2.0 + (_random.NextDouble() - 0.5) * 20 : 0,
                            Angle = found ? (_random.NextDouble() - 0.5) * 2.0 : 0
                        };
                        resultData = matchResult;
                        isPass = matchResult.Found;
                        score = matchResult.Score / 100.0;
                        break;

                    case Domain.Enums.VisionTaskType.BlobAnalysis:
                        var blobs = new List<BlobInfo>();
                        int blobCount = _random.Next(1, 6);
                        for (int i = 0; i < blobCount; i++)
                        {
                            blobs.Add(new BlobInfo
                            {
                                Id = i + 1,
                                Area = 100 + _random.Next(0, 5000),
                                CenterX = _random.Next(0, _imageWidth),
                                CenterY = _random.Next(0, _imageHeight),
                                BoundX = _random.Next(0, _imageWidth - 50),
                                BoundY = _random.Next(0, _imageHeight - 50),
                                BoundWidth = _random.Next(20, 100),
                                BoundHeight = _random.Next(20, 100),
                                Circularity = 0.5 + _random.NextDouble() * 0.5,
                                Perimeter = 100 + _random.NextDouble() * 200
                            });
                        }
                        resultData = blobs;
                        isPass = blobs.Count > 0;
                        score = 0.95;
                        break;

                    case Domain.Enums.VisionTaskType.Measurement:
                        double deviation = (_random.NextDouble() + _random.NextDouble() - 1.0) * 0.08;
                        var measurement = new MeasurementResult
                        {
                            MeasuredValue = Math.Round(10.0 + deviation, 4),
                            Deviation = Math.Round(deviation, 4),
                            IsInTolerance = Math.Abs(deviation) <= 0.1,
                            Confidence = 0.95 + _random.NextDouble() * 0.05,
                            Description = $"SimMeasure: {10.0 + deviation:F4}mm"
                        };
                        resultData = measurement;
                        isPass = measurement.IsInTolerance;
                        score = measurement.Confidence;
                        break;

                    case Domain.Enums.VisionTaskType.OCR:
                        string text = OcrStringPool[_random.Next(OcrStringPool.Length)];
                        var ocr = new OcrResult
                        {
                            Text = text,
                            Confidence = 0.90 + _random.NextDouble() * 0.10,
                            RecognitionType = "Text"
                        };
                        resultData = ocr;
                        isPass = ocr.Confidence > 0.8;
                        score = ocr.Confidence;
                        break;

                    case Domain.Enums.VisionTaskType.DefectDetection:
                        double roll = _random.NextDouble();
                        DefectResult defect;
                        if (roll < 0.95)
                        {
                            defect = new DefectResult
                            {
                                Judgment = DefectJudgment.OK,
                                Confidence = 0.95 + _random.NextDouble() * 0.05
                            };
                        }
                        else if (roll < 0.99)
                        {
                            var defects = new List<DefectItem>();
                            int dc = _random.Next(1, 3);
                            for (int i = 0; i < dc; i++)
                            {
                                defects.Add(new DefectItem
                                {
                                    DefectType = new[] { "划痕", "气泡", "污点", "裂纹" }[_random.Next(4)],
                                    X = _random.NextDouble() * _imageWidth,
                                    Y = _random.NextDouble() * _imageHeight,
                                    Area = 10 + _random.NextDouble() * 200,
                                    Severity = 0.3 + _random.NextDouble() * 0.7
                                });
                            }
                            defect = new DefectResult
                            {
                                Judgment = DefectJudgment.NG,
                                Defects = defects,
                                Confidence = 0.85 + _random.NextDouble() * 0.10
                            };
                        }
                        else
                        {
                            defect = new DefectResult
                            {
                                Judgment = DefectJudgment.Uncertain,
                                Confidence = 0.40 + _random.NextDouble() * 0.30
                            };
                        }
                        resultData = defect;
                        isPass = defect.Judgment != DefectJudgment.NG;
                        score = defect.Confidence;
                        break;

                    default:
                        resultData = new { Message = "未知任务类型" };
                        isPass = false;
                        score = 0;
                        break;
                }
            }

            sw.Stop();

            _ = _eventBus.PublishAsync(new HardwareVisionResultEvent(
                _engineId, task.TaskType.ToString(), isPass, score * 100));

            return VisionResult<object>.Success(resultData, sw.Elapsed);
        }

        // ==================== 模型管理 ====================

        /// <summary>
        /// 模拟加载视觉模型（记录模型ID和路径，模拟100ms加载延时）
        /// </summary>
        public async Task LoadModelAsync(string modelId, string modelPath, CancellationToken cancellationToken = default)
        {
            await Task.Delay(100, cancellationToken);
            _loadedModels[modelId] = modelPath;
        }

        // ==================== ROI 设置 ====================

        /// <summary>
        /// 设置指定任务的感兴趣区域
        /// </summary>
        public void SetROI(Guid taskId, VisionRegion region)
        {
            _roiMap[taskId] = region;
        }

        // ==================== 相机管理 ====================

        /// <summary>
        /// 返回模拟相机列表
        /// </summary>
        public Task<IReadOnlyList<string>> GetCameraList(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<string> cameras = SimCameraList.ToList().AsReadOnly();
            return Task.FromResult(cameras);
        }

        // ==================== 标定 ====================

        /// <summary>
        /// 模拟相机标定（使用最小二乘法计算仿射矩阵）
        /// </summary>
        public async Task<CalibrationData> CalibrateAsync(
            string cameraId,
            IEnumerable<(double PixelX, double PixelY, double MechX, double MechY)> calibrationPoints,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(300, cancellationToken);

            var points = calibrationPoints.ToList();
            double pixelSize = 0.01; // 模拟 0.01mm/pixel

            return new CalibrationData
            {
                AffineMatrix = new double[,]
                {
                    { pixelSize, 0, 0 },
                    { 0, pixelSize, 0 }
                },
                PixelSizeX = pixelSize,
                PixelSizeY = pixelSize,
                RmsError = 0.005,
                MaxError = 0.012,
                PointCount = points.Count,
                CalibratedAt = DateTime.Now,
                IsValid = points.Count >= 4
            };
        }

        // ==================== 保留的独立算法方法（供外部直接调用） ====================

        /// <summary>
        /// 模拟图像采集（无参版本，向后兼容）
        /// </summary>
        public async Task<ImageData> CaptureImageAsync(CancellationToken cancellationToken = default)
        {
            return await CaptureAsync(_engineId, cancellationToken);
        }

        /// <summary>
        /// 模拟模板匹配
        /// </summary>
        public async Task<VisionResult<TemplateMatchResult>> ExecutePatternMatchAsync(
            ImageData image,
            string templateId,
            CancellationToken cancellationToken = default)
        {
            EnsureReady();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await SimulateDelay(100, 200, cancellationToken);

            TemplateMatchResult matchResult;
            lock (_randomLock)
            {
                bool found = _random.NextDouble() > 0.05;
                matchResult = new TemplateMatchResult
                {
                    Found = found,
                    Score = found ? 85.0 + _random.NextDouble() * 15.0 : 0,
                    X = found ? _imageWidth / 2.0 + (_random.NextDouble() - 0.5) * 20 : 0,
                    Y = found ? _imageHeight / 2.0 + (_random.NextDouble() - 0.5) * 20 : 0,
                    Angle = found ? (_random.NextDouble() - 0.5) * 2.0 : 0
                };
            }

            sw.Stop();
            var result = VisionResult<TemplateMatchResult>.Success(matchResult, sw.Elapsed);

            _ = _eventBus.PublishAsync(new HardwareVisionResultEvent(
                _engineId, "PatternMatch", matchResult.Found, matchResult.Score));

            return result;
        }

        /// <summary>
        /// 模拟 Blob 分析
        /// </summary>
        public async Task<VisionResult<List<BlobInfo>>> ExecuteBlobAnalysisAsync(
            ImageData image,
            BlobAnalysisParameters parameters,
            CancellationToken cancellationToken = default)
        {
            EnsureReady();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await SimulateDelay(80, 150, cancellationToken);

            var blobs = new List<BlobInfo>();
            lock (_randomLock)
            {
                int blobCount = _random.Next(1, 6);
                for (int i = 0; i < blobCount; i++)
                {
                    int area = parameters.MinArea + _random.Next(0, 5000);
                    int bx = _random.Next(0, _imageWidth - 50);
                    int by = _random.Next(0, _imageHeight - 50);
                    int bw = _random.Next(20, 100);
                    int bh = _random.Next(20, 100);

                    blobs.Add(new BlobInfo
                    {
                        Id = i + 1,
                        Area = area,
                        CenterX = bx + bw / 2.0,
                        CenterY = by + bh / 2.0,
                        BoundX = bx, BoundY = by,
                        BoundWidth = bw, BoundHeight = bh,
                        Circularity = 0.5 + _random.NextDouble() * 0.5,
                        Perimeter = 2.0 * (bw + bh) * (1 + _random.NextDouble() * 0.1)
                    });
                }
            }
            sw.Stop();
            return VisionResult<List<BlobInfo>>.Success(blobs, sw.Elapsed);
        }

        /// <summary>
        /// 模拟尺寸测量
        /// </summary>
        public async Task<VisionResult<MeasurementResult>> ExecuteMeasurementAsync(
            ImageData image,
            MeasurementParameters parameters,
            CancellationToken cancellationToken = default)
        {
            EnsureReady();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await SimulateDelay(50, 120, cancellationToken);

            MeasurementResult measurement;
            lock (_randomLock)
            {
                double deviation = (_random.NextDouble() + _random.NextDouble() - 1.0) * 0.08;
                double measured = parameters.NominalValue + deviation;
                bool inTol = deviation >= parameters.LowerTolerance && deviation <= parameters.UpperTolerance;

                measurement = new MeasurementResult
                {
                    MeasuredValue = Math.Round(measured, 4),
                    Deviation = Math.Round(deviation, 4),
                    IsInTolerance = inTol,
                    Confidence = 0.95 + _random.NextDouble() * 0.05,
                    Description = $"{parameters.MeasureType}: {measured:F4}mm"
                };
            }
            sw.Stop();
            return VisionResult<MeasurementResult>.Success(measurement, sw.Elapsed);
        }

        /// <summary>
        /// 模拟OCR识别
        /// </summary>
        public async Task<VisionResult<OcrResult>> ExecuteOcrAsync(
            ImageData image,
            CancellationToken cancellationToken = default)
        {
            EnsureReady();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await SimulateDelay(60, 130, cancellationToken);

            OcrResult ocrResult;
            lock (_randomLock)
            {
                string text = OcrStringPool[_random.Next(OcrStringPool.Length)];
                ocrResult = new OcrResult
                {
                    Text = text,
                    Confidence = 0.90 + _random.NextDouble() * 0.10,
                    RecognitionType = "Text"
                };
            }
            sw.Stop();
            return VisionResult<OcrResult>.Success(ocrResult, sw.Elapsed);
        }

        /// <summary>
        /// 模拟缺陷检测
        /// </summary>
        public async Task<VisionResult<DefectResult>> ExecuteDefectDetectionAsync(
            ImageData image,
            CancellationToken cancellationToken = default)
        {
            EnsureReady();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await SimulateDelay(100, 200, cancellationToken);

            DefectResult defectResult;
            lock (_randomLock)
            {
                double roll = _random.NextDouble();
                if (roll < 0.95)
                {
                    defectResult = new DefectResult
                    {
                        Judgment = DefectJudgment.OK,
                        Confidence = 0.95 + _random.NextDouble() * 0.05
                    };
                }
                else if (roll < 0.99)
                {
                    int defectCount = _random.Next(1, 3);
                    var defects = new List<DefectItem>();
                    for (int i = 0; i < defectCount; i++)
                    {
                        defects.Add(new DefectItem
                        {
                            DefectType = new[] { "划痕", "气泡", "污点", "裂纹" }[_random.Next(4)],
                            X = _random.NextDouble() * _imageWidth,
                            Y = _random.NextDouble() * _imageHeight,
                            Area = 10 + _random.NextDouble() * 200,
                            Severity = 0.3 + _random.NextDouble() * 0.7
                        });
                    }
                    defectResult = new DefectResult
                    {
                        Judgment = DefectJudgment.NG,
                        Defects = defects,
                        Confidence = 0.85 + _random.NextDouble() * 0.10
                    };
                }
                else
                {
                    defectResult = new DefectResult
                    {
                        Judgment = DefectJudgment.Uncertain,
                        Confidence = 0.40 + _random.NextDouble() * 0.30
                    };
                }
            }
            sw.Stop();

            _ = _eventBus.PublishAsync(new HardwareVisionResultEvent(
                _engineId, "DefectDetection",
                defectResult.Judgment != DefectJudgment.NG,
                defectResult.Confidence * 100));

            return VisionResult<DefectResult>.Success(defectResult, sw.Elapsed);
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 检查引擎就绪状态
        /// </summary>
        private void EnsureReady()
        {
            if (!_isReady) throw new InvalidOperationException($"视觉引擎[{_engineId}]未初始化");
        }

        /// <summary>
        /// 模拟随机延时（minMs ~ maxMs 均匀分布）
        /// </summary>
        private async Task SimulateDelay(int minMs, int maxMs, CancellationToken ct)
        {
            int delay;
            lock (_randomLock) { delay = _random.Next(minMs, maxMs + 1); }
            await Task.Delay(delay, ct);
        }

        // ==================== IAsyncDisposable ====================

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                _isReady = false;
                _loadedModels.Clear();
                _roiMap.Clear();
            }
            return ValueTask.CompletedTask;
        }
    }
}
