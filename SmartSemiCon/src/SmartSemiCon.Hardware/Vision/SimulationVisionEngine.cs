// ============================================================
// 文件：SimulationVisionEngine.cs
// 用途：模拟视觉引擎 + 相机管理器
// 设计思路：
//   视觉系统架构分为三层：
//   1. Camera层 — 管理物理相机的打开/关闭/采集
//   2. Engine层 — 视觉算法处理（模板匹配/Blob/OCR等）
//   3. Pipeline层 — 图像缓冲队列，解耦采集和处理
//
//   本文件提供完整的模拟实现，生成模拟图像数据和检测结果。
//   替换为真实视觉库只需实现 IVisionEngine 和 ICamera 接口。
// ============================================================

using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.Hardware.Vision
{
    /// <summary>
    /// 模拟相机 — 无需硬件，生成模拟图像数据。
    /// </summary>
    public class SimulationCamera : ICamera
    {
        private bool _isOpened;
        private CancellationTokenSource? _continuousCts;
        private readonly Random _random = new();

        /// <summary>相机配置</summary>
        public CameraConfig Config { get; }

        /// <summary>是否已打开</summary>
        public bool IsOpened => _isOpened;

        /// <summary>图像采集事件</summary>
        public event EventHandler<byte[]>? ImageCaptured;

        public SimulationCamera(CameraConfig config)
        {
            Config = config;
        }

        /// <summary>打开相机。</summary>
        public Task<bool> OpenAsync()
        {
            _isOpened = true;
            return Task.FromResult(true);
        }

        /// <summary>关闭相机。</summary>
        public Task CloseAsync()
        {
            _continuousCts?.Cancel();
            _isOpened = false;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 采集一帧图像 — 生成模拟灰度图像数据。
        /// 模拟一个包含简单几何形状的图像。
        /// </summary>
        public async Task<byte[]?> CaptureAsync(CancellationToken cancellationToken = default)
        {
            if (!_isOpened) return null;

            // 模拟曝光延迟
            await Task.Delay((int)(Config.ExposureTime / 1000), cancellationToken);

            // 生成模拟灰度图像（宽x高 字节数组）
            var imageData = new byte[Config.ImageWidth * Config.ImageHeight];

            // 填充背景灰度
            Array.Fill(imageData, (byte)128);

            // 在图像中心绘制一个模拟的明亮圆形（模拟Mark点）
            var centerX = Config.ImageWidth / 2 + _random.Next(-10, 10);
            var centerY = Config.ImageHeight / 2 + _random.Next(-10, 10);
            var radius = 50;

            for (int y = 0; y < Config.ImageHeight; y++)
            {
                for (int x = 0; x < Config.ImageWidth; x++)
                {
                    var dist = Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2));
                    if (dist < radius)
                    {
                        imageData[y * Config.ImageWidth + x] = 255;
                    }
                }
            }

            ImageCaptured?.Invoke(this, imageData);
            return imageData;
        }

        /// <summary>设置曝光时间。</summary>
        public Task SetExposureAsync(double exposureUs)
        {
            // 模拟相机配置
            return Task.CompletedTask;
        }

        /// <summary>设置增益。</summary>
        public Task SetGainAsync(double gain)
        {
            return Task.CompletedTask;
        }

        /// <summary>设置触发模式。</summary>
        public Task SetTriggerModeAsync(TriggerMode mode)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            CloseAsync().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 模拟视觉引擎 — 模拟Halcon/OpenCV/VisionPro的算法调用。
    /// 返回带有随机偏差的模拟检测结果。
    /// </summary>
    public class SimulationVisionEngine : IVisionEngine
    {
        private readonly Random _random = new();

        /// <summary>引擎类型</summary>
        public VisionEngineType EngineType => VisionEngineType.Simulation;

        /// <summary>是否已初始化</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>初始化引擎。</summary>
        public Task<bool> InitializeAsync()
        {
            IsInitialized = true;
            return Task.FromResult(true);
        }

        /// <summary>
        /// 模板匹配 — 在图像中查找模板位置。
        /// 返回模拟的匹配结果（中心附近加随机偏差）。
        /// </summary>
        public async Task<VisionResult> TemplateMatchAsync(byte[] image, int width, int height, object templateData)
        {
            await Task.Delay(50); // 模拟处理耗时

            return new VisionResult
            {
                IsSuccess = true,
                PixelX = width / 2.0 + _random.NextDouble() * 5 - 2.5,
                PixelY = height / 2.0 + _random.NextDouble() * 5 - 2.5,
                Angle = _random.NextDouble() * 2 - 1,
                Score = 0.85 + _random.NextDouble() * 0.15,
                ProcessTimeMs = 45 + _random.NextDouble() * 20
            };
        }

        /// <summary>Blob分析 — 二值化后提取连通区域。</summary>
        public async Task<VisionResult> BlobAnalysisAsync(byte[] image, int width, int height, double threshold)
        {
            await Task.Delay(30);

            return new VisionResult
            {
                IsSuccess = true,
                PixelX = width / 2.0 + _random.NextDouble() * 10 - 5,
                PixelY = height / 2.0 + _random.NextDouble() * 10 - 5,
                Score = 0.9 + _random.NextDouble() * 0.1,
                ProcessTimeMs = 25 + _random.NextDouble() * 15,
                ExtraData = new Dictionary<string, object>
                {
                    ["BlobCount"] = _random.Next(1, 5),
                    ["BlobArea"] = 2500 + _random.NextDouble() * 500
                }
            };
        }

        /// <summary>OCR识别。</summary>
        public async Task<VisionResult> OcrAsync(byte[] image, int width, int height, object ocrModel)
        {
            await Task.Delay(80);

            return new VisionResult
            {
                IsSuccess = true,
                Score = 0.92,
                ProcessTimeMs = 75,
                ExtraData = new Dictionary<string, object>
                {
                    ["Text"] = "SN2024001",
                    ["Confidence"] = 0.95
                }
            };
        }

        /// <summary>Mark点定位。</summary>
        public async Task<VisionResult> FindMarkAsync(byte[] image, int width, int height)
        {
            await Task.Delay(40);

            return new VisionResult
            {
                IsSuccess = true,
                PixelX = width / 2.0 + _random.NextDouble() * 3 - 1.5,
                PixelY = height / 2.0 + _random.NextDouble() * 3 - 1.5,
                Angle = _random.NextDouble() * 0.5,
                Score = 0.95,
                ProcessTimeMs = 35
            };
        }

        /// <summary>尺寸测量。</summary>
        public async Task<VisionResult> MeasureAsync(byte[] image, int width, int height, object measureConfig)
        {
            await Task.Delay(60);

            return new VisionResult
            {
                IsSuccess = true,
                ProcessTimeMs = 55,
                ExtraData = new Dictionary<string, object>
                {
                    ["Distance"] = 25.03 + _random.NextDouble() * 0.1,
                    ["Unit"] = "mm"
                }
            };
        }

        /// <summary>缺陷检测。</summary>
        public async Task<VisionResult> DefectDetectAsync(byte[] image, int width, int height, object detectConfig)
        {
            await Task.Delay(100);

            var hasDefect = _random.NextDouble() < 0.1; // 10%概率检出缺陷

            return new VisionResult
            {
                IsSuccess = true,
                Score = hasDefect ? 0.3 : 0.95,
                ProcessTimeMs = 95,
                ExtraData = new Dictionary<string, object>
                {
                    ["HasDefect"] = hasDefect,
                    ["DefectCount"] = hasDefect ? _random.Next(1, 3) : 0,
                    ["DefectType"] = hasDefect ? "Scratch" : "None"
                }
            };
        }

        public void Dispose() { GC.SuppressFinalize(this); }
    }

    /// <summary>
    /// 相机管理器 — 管理所有相机的统一入口。
    /// </summary>
    public class CameraManager : IDisposable
    {
        private readonly Dictionary<int, ICamera> _cameras = new();

        /// <summary>相机数量</summary>
        public int CameraCount => _cameras.Count;

        /// <summary>
        /// 添加相机。
        /// </summary>
        public void AddCamera(CameraConfig config)
        {
            ICamera camera = config.EngineType switch
            {
                VisionEngineType.Simulation => new SimulationCamera(config),
                _ => new SimulationCamera(config)
            };
            _cameras[config.CameraId] = camera;
        }

        /// <summary>获取指定相机。</summary>
        public ICamera? GetCamera(int cameraId)
        {
            return _cameras.TryGetValue(cameraId, out var camera) ? camera : null;
        }

        /// <summary>打开所有相机。</summary>
        public async Task<bool> OpenAllAsync()
        {
            foreach (var camera in _cameras.Values)
            {
                if (!await camera.OpenAsync()) return false;
            }
            return true;
        }

        /// <summary>关闭所有相机。</summary>
        public async Task CloseAllAsync()
        {
            foreach (var camera in _cameras.Values)
            {
                await camera.CloseAsync();
            }
        }

        /// <summary>
        /// 创建默认相机配置（用于演示）。
        /// </summary>
        public static List<CameraConfig> CreateDefaultConfigs()
        {
            return new List<CameraConfig>
            {
                new() { CameraId = 0, Name = "顶部相机", ImageWidth = 2048, ImageHeight = 1536, ExposureTime = 3000 },
                new() { CameraId = 1, Name = "底部相机", ImageWidth = 2048, ImageHeight = 1536, ExposureTime = 5000 },
                new() { CameraId = 2, Name = "侧面相机", ImageWidth = 1920, ImageHeight = 1080, ExposureTime = 4000 },
                new() { CameraId = 3, Name = "定位相机", ImageWidth = 2448, ImageHeight = 2048, ExposureTime = 2000 }
            };
        }

        public void Dispose()
        {
            foreach (var camera in _cameras.Values) camera.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
