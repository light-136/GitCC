// ============================================================
// 文件：CameraService.cs
// 用途：相机抽象层 — 统一接口 + 模拟相机实现
// 设计思路：
//   工业相机品牌众多（Basler、FLIR、大恒、海康等），
//   通过 ICameraService 接口抽象相机操作，使上层代码不依赖具体硬件。
//   本文件提供：
//   1. SimulatedCamera — 模拟相机，生成测试图像
//   2. CameraFactory — 相机工厂，按类型创建相机实例
//   模拟相机可以生成纯色、渐变、棋盘格等测试图案。
// ============================================================

using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Modules.Vision
{
    /// <summary>
    /// 模拟相机测试图案类型。
    /// </summary>
    public enum TestPattern
    {
        /// <summary>灰色均匀图像。</summary>
        SolidGray,
        /// <summary>水平渐变。</summary>
        HorizontalGradient,
        /// <summary>垂直渐变。</summary>
        VerticalGradient,
        /// <summary>棋盘格图案。</summary>
        Checkerboard,
        /// <summary>随机噪声。</summary>
        RandomNoise,
        /// <summary>包含圆形目标的测试图像。</summary>
        CircleTarget
    }

    /// <summary>
    /// 模拟相机 — 生成测试图像用于开发和调试。
    /// 实现 ICameraService 接口，可以替代真实相机使用。
    /// </summary>
    public class SimulatedCamera : ICameraService
    {
        private bool _isConnected;
        private readonly Random _random = new(42);
        private readonly CameraConfig _config;

        /// <summary>测试图案类型。</summary>
        public TestPattern Pattern { get; set; } = TestPattern.CircleTarget;

        /// <summary>图像宽度（像素）。</summary>
        public int Width => _config.Width;

        /// <summary>图像高度（像素）。</summary>
        public int Height => _config.Height;

        /// <summary>曝光时间（毫秒）。</summary>
        public double ExposureMs { get; set; }

        /// <summary>增益。</summary>
        public double Gain { get; set; }

        /// <summary>是否已连接。</summary>
        public bool IsConnected => _isConnected;

        /// <summary>图像采集完成时触发。</summary>
        public event EventHandler<ImageData>? ImageCaptured;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="config">相机配置（可选，默认640×480）。</param>
        public SimulatedCamera(CameraConfig? config = null)
        {
            _config = config ?? new CameraConfig
            {
                CameraId = "SimCamera-001",
                DriverType = "Simulated",
                Width = 640,
                Height = 480
            };
            ExposureMs = _config.ExposureMs;
            Gain = _config.Gain;
        }

        public Task ConnectAsync(CameraConfig config)
        {
            _isConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            _isConnected = false;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 采集图像 — 根据当前测试图案类型生成图像。
        /// </summary>
        public Task<ImageData> CaptureAsync()
        {
            if (!_isConnected)
                throw new InvalidOperationException("相机未连接");

            var image = Pattern switch
            {
                TestPattern.SolidGray => GenerateSolidGray(),
                TestPattern.HorizontalGradient => GenerateHGradient(),
                TestPattern.VerticalGradient => GenerateVGradient(),
                TestPattern.Checkerboard => GenerateCheckerboard(),
                TestPattern.RandomNoise => GenerateNoise(),
                TestPattern.CircleTarget => GenerateCircleTarget(),
                _ => GenerateSolidGray()
            };

            // 触发图像采集完成事件
            ImageCaptured?.Invoke(this, image);

            return Task.FromResult(image);
        }

        // ========== 图案生成 ==========

        private ImageData GenerateSolidGray()
        {
            var img = ImageData.Create(Width, Height, 1);
            byte gray = (byte)(128 * ExposureMs / 10.0);
            Array.Fill(img.Pixels, gray);
            return img;
        }

        private ImageData GenerateHGradient()
        {
            var img = ImageData.Create(Width, Height, 1);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    img.Pixels[y * Width + x] = (byte)(x * 255 / Width);
            return img;
        }

        private ImageData GenerateVGradient()
        {
            var img = ImageData.Create(Width, Height, 1);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    img.Pixels[y * Width + x] = (byte)(y * 255 / Height);
            return img;
        }

        private ImageData GenerateCheckerboard()
        {
            var img = ImageData.Create(Width, Height, 1);
            int cellSize = 40;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    bool white = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                    img.Pixels[y * Width + x] = white ? (byte)230 : (byte)25;
                }
            return img;
        }

        private ImageData GenerateNoise()
        {
            var img = ImageData.Create(Width, Height, 1);
            _random.NextBytes(img.Pixels);
            return img;
        }

        /// <summary>
        /// 生成包含圆形目标的测试图像 — 白色背景上的黑色圆形。
        /// 用于 Blob 分析和圆拟合测试。
        /// </summary>
        private ImageData GenerateCircleTarget()
        {
            var img = ImageData.Create(Width, Height, 1);
            Array.Fill(img.Pixels, (byte)200); // 浅灰背景

            // 绘制3个不同大小的圆形
            DrawCircle(img, Width / 4, Height / 4, 30, 20);
            DrawCircle(img, Width / 2, Height / 2, 50, 15);
            DrawCircle(img, 3 * Width / 4, 3 * Height / 4, 20, 25);

            return img;
        }

        /// <summary>
        /// 在图像上绘制填充圆。
        /// </summary>
        private static void DrawCircle(ImageData img, int cx, int cy, int radius, byte value)
        {
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (x < 0 || x >= img.Width || y < 0 || y >= img.Height) continue;
                    double dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (dist <= radius)
                    {
                        img.Pixels[y * img.Stride + x] = value;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 相机工厂 — 按类型创建相机实例。
    /// 扩展时在此添加新相机类型（如 Basler、海康等）。
    /// </summary>
    public static class CameraFactory
    {
        /// <summary>
        /// 创建相机实例。
        /// </summary>
        /// <param name="config">相机配置。</param>
        /// <returns>相机服务实例。</returns>
        public static ICameraService Create(CameraConfig config)
        {
            return config.DriverType?.ToLowerInvariant() switch
            {
                "simulated" or "sim" or null => new SimulatedCamera(config),
                // 扩展点：添加更多相机类型
                // "basler" => new BaslerCamera(config),
                // "hikvision" => new HikvisionCamera(config),
                _ => new SimulatedCamera(config)
            };
        }
    }
}
