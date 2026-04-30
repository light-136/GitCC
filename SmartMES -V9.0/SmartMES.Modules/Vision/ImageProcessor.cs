// ============================================================
// 文件：ImageProcessor.cs
// 用途：基础图像处理 — 灰度化、高斯模糊、中值滤波、Sobel边缘检测
// 设计思路：
//   纯 C# 实现基础图像处理算法，不依赖任何第三方库。
//   所有操作基于 ImageData（纯字节数组图像），可单元测试。
//
//   算法说明：
//   1. 灰度化：加权平均法 Gray = 0.299R + 0.587G + 0.114B
//   2. 高斯模糊：5×5 卷积核，σ=1.4
//   3. 中值滤波：取邻域中值，去除椒盐噪声
//   4. Sobel 边缘：Gx + Gy 梯度计算，输出边缘强度
// ============================================================

using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Modules.Vision
{
    /// <summary>
    /// 灰度化处理器 — 将彩色图像转换为灰度图像。
    /// 公式：Gray = 0.299 × R + 0.587 × G + 0.114 × B（ITU-R BT.601）
    /// </summary>
    public class GrayscaleProcessor : IImageProcessor
    {
        public string Name => "灰度化";

        public ImageData Process(ImageData input, Dictionary<string, object>? parameters = null)
        {
            if (input.Channels == 1)
                return input.Clone();

            var output = ImageData.Create(input.Width, input.Height, 1);

            for (int y = 0; y < input.Height; y++)
            {
                for (int x = 0; x < input.Width; x++)
                {
                    int srcIdx = y * input.Stride + x * input.Channels;
                    byte b = input.Pixels[srcIdx];
                    byte g = input.Pixels[srcIdx + 1];
                    byte r = input.Pixels[srcIdx + 2];

                    // ITU-R BT.601 加权平均
                    byte gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                    output.Pixels[y * output.Stride + x] = gray;
                }
            }

            return output;
        }
    }

    /// <summary>
    /// 高斯模糊处理器 — 5×5 高斯卷积核平滑图像。
    /// 核参数 σ=1.4，归一化权重总和为159。
    /// 作用：减少噪声，平滑图像。
    /// </summary>
    public class GaussianBlurProcessor : IImageProcessor
    {
        public string Name => "高斯模糊";

        // 5×5 高斯核（σ≈1.4）
        private static readonly int[,] Kernel = {
            { 2,  4,  5,  4, 2 },
            { 4,  9, 12,  9, 4 },
            { 5, 12, 15, 12, 5 },
            { 4,  9, 12,  9, 4 },
            { 2,  4,  5,  4, 2 }
        };
        private const int KernelSum = 159; // 核权重总和

        public ImageData Process(ImageData input, Dictionary<string, object>? parameters = null)
        {
            // 仅处理灰度图
            int w = input.Width, h = input.Height;
            int channels = input.Channels;
            var output = ImageData.Create(w, h, channels);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        int sum = 0;
                        for (int ky = -2; ky <= 2; ky++)
                        {
                            for (int kx = -2; kx <= 2; kx++)
                            {
                                // 边界钳位（超出范围使用边缘像素值）
                                int sy = Math.Clamp(y + ky, 0, h - 1);
                                int sx = Math.Clamp(x + kx, 0, w - 1);
                                sum += input.Pixels[sy * input.Stride + sx * channels + c]
                                       * Kernel[ky + 2, kx + 2];
                            }
                        }
                        output.Pixels[y * output.Stride + x * channels + c] =
                            (byte)Math.Clamp(sum / KernelSum, 0, 255);
                    }
                }
            }

            return output;
        }
    }

    /// <summary>
    /// 中值滤波处理器 — 取邻域像素中值，去除椒盐噪声。
    /// 核大小默认 3×3。
    /// </summary>
    public class MedianFilterProcessor : IImageProcessor
    {
        public string Name => "中值滤波";

        /// <summary>滤波核半径（默认1，即3×3窗口）。</summary>
        public int Radius { get; set; } = 1;

        public ImageData Process(ImageData input, Dictionary<string, object>? parameters = null)
        {
            int w = input.Width, h = input.Height;
            int channels = input.Channels;
            var output = ImageData.Create(w, h, channels);
            int size = (2 * Radius + 1) * (2 * Radius + 1);
            var buffer = new byte[size];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        int idx = 0;
                        for (int ky = -Radius; ky <= Radius; ky++)
                        {
                            for (int kx = -Radius; kx <= Radius; kx++)
                            {
                                int sy = Math.Clamp(y + ky, 0, h - 1);
                                int sx = Math.Clamp(x + kx, 0, w - 1);
                                buffer[idx++] = input.Pixels[sy * input.Stride + sx * channels + c];
                            }
                        }

                        // 排序取中值
                        Array.Sort(buffer, 0, size);
                        output.Pixels[y * output.Stride + x * channels + c] = buffer[size / 2];
                    }
                }
            }

            return output;
        }
    }

    /// <summary>
    /// Sobel 边缘检测处理器 — 计算图像梯度强度。
    /// 使用 3×3 Sobel 算子分别计算 X 和 Y 方向梯度，
    /// 然后合成边缘强度：G = sqrt(Gx² + Gy²)。
    /// </summary>
    public class SobelEdgeProcessor : IImageProcessor
    {
        public string Name => "Sobel边缘检测";

        // Sobel X 方向算子
        private static readonly int[,] SobelX = {
            { -1, 0, 1 },
            { -2, 0, 2 },
            { -1, 0, 1 }
        };

        // Sobel Y 方向算子
        private static readonly int[,] SobelY = {
            { -1, -2, -1 },
            {  0,  0,  0 },
            {  1,  2,  1 }
        };

        public ImageData Process(ImageData input, Dictionary<string, object>? parameters = null)
        {
            int w = input.Width, h = input.Height;
            var output = ImageData.Create(w, h, 1);

            // Sobel 要求灰度图，如果是彩色图先取第一通道
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int gx = 0, gy = 0;
                    for (int ky = -1; ky <= 1; ky++)
                    {
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            byte pixel = input.Pixels[(y + ky) * input.Stride + (x + kx) * input.Channels];
                            gx += pixel * SobelX[ky + 1, kx + 1];
                            gy += pixel * SobelY[ky + 1, kx + 1];
                        }
                    }

                    // 梯度强度
                    int magnitude = (int)Math.Sqrt(gx * gx + gy * gy);
                    output.Pixels[y * output.Stride + x] = (byte)Math.Clamp(magnitude, 0, 255);
                }
            }

            return output;
        }
    }

    /// <summary>
    /// 二值化处理器 — 将灰度图像转换为黑白二值图像。
    /// 像素值 > 阈值 → 255（白），否则 → 0（黑）。
    /// </summary>
    public class ThresholdProcessor : IImageProcessor
    {
        public string Name => "二值化";

        /// <summary>阈值（0~255）。</summary>
        public byte ThresholdValue { get; set; } = 128;

        /// <summary>是否反转（暗物体为白）。</summary>
        public bool Invert { get; set; }

        public ImageData Process(ImageData input, Dictionary<string, object>? parameters = null)
        {
            var output = ImageData.Create(input.Width, input.Height, 1);

            for (int i = 0; i < input.Width * input.Height; i++)
            {
                byte pixel = input.Pixels[i * input.Channels];
                bool above = pixel > ThresholdValue;
                if (Invert) above = !above;
                output.Pixels[i] = above ? (byte)255 : (byte)0;
            }

            return output;
        }
    }
}
