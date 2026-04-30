// ============================================================
// 文件：VisionEngineV2.cs
// 用途：V2 视觉引擎 — 向后兼容门面，组合所有新子系统
// 设计思路：
//   VisionEngineV2 作为视觉系统的统一入口，组合以下子模块：
//   - 图像处理器集合（灰度化、高斯、中值、Sobel、阈值、形态学）
//   - 直方图分析器
//   - 模板匹配器
//   - Blob 分析器
//   - 测量工具
//   - 管线执行器
//   - 相机服务
//   - 标定服务
//   提供统一的API，简化上层调用。
// ============================================================

using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Modules.Vision
{
    /// <summary>
    /// V2 视觉引擎 — 组合所有视觉子系统的统一门面。
    ///
    /// 使用示例：
    ///   var engine = new VisionEngineV2();
    ///   await engine.ConnectCameraAsync();
    ///   var image = await engine.CaptureAsync();
    ///   var gray = engine.ToGrayscale(image);
    ///   var blobs = engine.FindBlobs(gray);
    ///   var circles = engine.FitCircles(blobs, gray);
    /// </summary>
    public class VisionEngineV2 : IDisposable
    {
        // ========== 子模块实例 ==========

        /// <summary>模板匹配器。</summary>
        public TemplateMatcher TemplateMatcher { get; } = new();

        /// <summary>Blob 分析器。</summary>
        public BlobAnalyzer BlobAnalyzer { get; } = new();

        /// <summary>标定服务。</summary>
        public CalibrationService CalibrationService { get; } = new();

        /// <summary>图像处理管线。</summary>
        public VisionPipeline Pipeline { get; } = new();

        /// <summary>相机服务。</summary>
        public ICameraService Camera { get; private set; }

        // 预构建的处理器实例
        private readonly GrayscaleProcessor _grayscale = new();
        private readonly GaussianBlurProcessor _gaussianBlur = new();
        private readonly MedianFilterProcessor _medianFilter = new();
        private readonly SobelEdgeProcessor _sobelEdge = new();
        private readonly OtsuThresholdProcessor _otsu = new();

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>
        /// 构造函数 — 使用模拟相机。
        /// </summary>
        public VisionEngineV2(CameraConfig? cameraConfig = null)
        {
            Camera = CameraFactory.Create(cameraConfig ?? new CameraConfig
            {
                CameraId = "SimCamera",
                DriverType = "Simulated",
                Width = 640,
                Height = 480
            });
        }

        /// <summary>
        /// 构造函数 — 使用自定义相机服务。
        /// </summary>
        public VisionEngineV2(ICameraService camera)
        {
            Camera = camera ?? throw new ArgumentNullException(nameof(camera));
        }

        // ========== 相机操作 ==========

        /// <summary>连接相机。</summary>
        public async Task ConnectCameraAsync(CameraConfig? config = null)
        {
            var cfg = config ?? new CameraConfig
            {
                CameraId = "SimCamera",
                DriverType = "Simulated",
                Width = 640,
                Height = 480
            };
            await Camera.ConnectAsync(cfg);
            Log("[视觉] 相机已连接");
        }

        /// <summary>断开相机。</summary>
        public async Task DisconnectCameraAsync()
        {
            await Camera.DisconnectAsync();
            Log("[视觉] 相机已断开");
        }

        /// <summary>采集图像。</summary>
        public async Task<ImageData> CaptureAsync()
        {
            var image = await Camera.CaptureAsync();
            Log($"[视觉] 采集图像：{image.Width}×{image.Height}×{image.Channels}");
            return image;
        }

        // ========== 图像处理快捷方法 ==========

        /// <summary>转灰度。</summary>
        public ImageData ToGrayscale(ImageData image)
            => _grayscale.Process(image);

        /// <summary>高斯模糊。</summary>
        public ImageData GaussianBlur(ImageData image)
            => _gaussianBlur.Process(image);

        /// <summary>中值滤波。</summary>
        public ImageData MedianFilter(ImageData image)
            => _medianFilter.Process(image);

        /// <summary>Sobel 边缘检测。</summary>
        public ImageData SobelEdge(ImageData image)
            => _sobelEdge.Process(image);

        /// <summary>OTSU 自动阈值二值化。</summary>
        public ImageData OtsuThreshold(ImageData image)
            => _otsu.Process(image);

        /// <summary>
        /// 形态学操作。
        /// </summary>
        public ImageData Morphology(ImageData image, MorphologyOperation op,
                                     int kernelSize = 3, int iterations = 1)
        {
            var processor = new MorphologyProcessor
            {
                Operation = op,
                KernelSize = kernelSize,
                Iterations = iterations
            };
            return processor.Process(image);
        }

        /// <summary>
        /// 固定阈值二值化。
        /// </summary>
        public ImageData Threshold(ImageData image, byte threshold, bool invert = false)
        {
            var processor = new ThresholdProcessor
            {
                ThresholdValue = threshold,
                Invert = invert
            };
            return processor.Process(image);
        }

        // ========== 分析工具 ==========

        /// <summary>
        /// 计算直方图。
        /// </summary>
        public HistogramData ComputeHistogram(ImageData image)
            => HistogramAnalyzer.ComputeHistogram(image);

        /// <summary>
        /// 计算 OTSU 阈值。
        /// </summary>
        public byte ComputeOtsuThreshold(ImageData image)
            => HistogramAnalyzer.ComputeOtsuThreshold(image);

        /// <summary>
        /// 模板匹配。
        /// </summary>
        public List<TemplateMatchResult> MatchTemplate(ImageData image, ImageData template)
            => TemplateMatcher.Match(image, template);

        /// <summary>
        /// Blob 分析。
        /// </summary>
        public List<BlobInfo> FindBlobs(ImageData binaryImage)
            => BlobAnalyzer.Analyze(binaryImage);

        // ========== 测量 ==========

        /// <summary>测量两点距离。</summary>
        public double MeasureDistance(double x1, double y1, double x2, double y2)
            => MeasurementTools.MeasureDistance(x1, y1, x2, y2).Value;

        /// <summary>测量三点夹角（度）。</summary>
        public double MeasureAngle(double x1, double y1, double x2, double y2, double x3, double y3)
            => MeasurementTools.MeasureAngle(x1, y1, x2, y2, x3, y3).Value;

        /// <summary>圆拟合。</summary>
        public (double CenterX, double CenterY, double Radius, double Error) FitCircle(
            List<(double X, double Y)> points)
            => MeasurementTools.FitCircle(points);

        /// <summary>直线拟合。</summary>
        public (double K, double B, double Error) FitLine(
            List<(double X, double Y)> points)
            => MeasurementTools.FitLine(points);

        // ========== 标定 ==========

        /// <summary>执行标定。</summary>
        public CalibrationData Calibrate(List<CalibrationPointPair> points)
            => CalibrationService.Calibrate(points);

        /// <summary>像素坐标转物理坐标。</summary>
        public (double WorldX, double WorldY) PixelToWorld(double pixelX, double pixelY)
            => CalibrationService.PixelToWorld(pixelX, pixelY);

        // ========== 管线 ==========

        /// <summary>
        /// 构建默认检测管线（灰度→模糊→OTSU→形态学）。
        /// </summary>
        public void BuildDefaultPipeline()
        {
            Pipeline.Clear();
            Pipeline.AddStep(_grayscale);
            Pipeline.AddStep(_gaussianBlur);
            Pipeline.AddStep(_otsu);
            Pipeline.AddStep(new MorphologyProcessor
            {
                Operation = MorphologyOperation.Open,
                KernelSize = 3
            });
            Log("[视觉] 默认管线已构建：灰度 → 高斯模糊 → OTSU → 开运算");
        }

        /// <summary>
        /// 执行管线。
        /// </summary>
        public ImageData RunPipeline(ImageData input)
            => Pipeline.Execute(input);

        /// <summary>
        /// 执行管线（带诊断）。
        /// </summary>
        public List<PipelineStepResult> RunPipelineWithDiagnostics(ImageData input)
            => Pipeline.ExecuteWithDiagnostics(input);

        // ========== 辅助 ==========

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);

        public void Dispose()
        {
            if (Camera is IDisposable d)
                d.Dispose();
        }
    }
}
