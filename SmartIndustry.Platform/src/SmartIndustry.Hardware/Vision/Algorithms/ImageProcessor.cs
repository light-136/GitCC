// ============================================================
// 文件：ImageProcessor.cs
// 层级：硬件抽象层（Hardware Layer）> Vision > Algorithms
// 职责：图像处理算法库（纯数学实现，不依赖 OpenCV/Halcon 等第三方库）。
//       操作对象为 byte[] 图像数据（灰度图，1通道）。
//
// 包含算法：
//   1. 灰度直方图计算（含均值、标准差、Otsu阈值）
//   2. 固定阈值二值化
//   3. 大津法（Otsu）自动阈值二值化
//   4. 均值滤波（可配置窗口大小）
//   5. 高斯滤波（3×3 和 5×5 预定义核）
//   6. Sobel 边缘检测（水平+垂直梯度合并）
//
// 性能说明：
//   纯 C# 实现，小图像（640×480）各算法 < 10ms，
//   大图像（2048×1536）约 50~200ms，
//   如需高性能请替换为 OpenCvSharp 或 Emgu.CV。
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Models;

namespace SmartIndustry.Hardware.Vision.Algorithms
{
    /// <summary>
    /// 图像处理算法静态库。
    /// 所有方法均为静态方法，操作 byte[] 灰度图像。
    /// 输入图像不被修改（Copy-on-write），所有方法返回新图像。
    /// </summary>
    public static class ImageProcessor
    {
        // ==================== 1. 灰度直方图 ====================

        /// <summary>
        /// 计算灰度图像的直方图。
        /// 统计每个灰度级（0~255）的像素数量，以及均值、标准差、Otsu阈值。
        /// </summary>
        /// <param name="image">输入灰度图像（Channels 必须为1）</param>
        /// <returns>直方图数据</returns>
        public static HistogramData ComputeHistogram(ImageData image)
        {
            if (image.Channels != 1)
                throw new ArgumentException("灰度直方图仅支持单通道灰度图像");

            int[] bins = new int[256];
            byte[] pixels = image.Pixels;
            int total = pixels.Length;

            // 统计各灰度级像素数
            for (int i = 0; i < total; i++)
                bins[pixels[i]]++;

            // 计算均值
            double sum = 0;
            for (int i = 0; i < 256; i++) sum += i * bins[i];
            double mean = sum / total;

            // 计算标准差
            double variance = 0;
            for (int i = 0; i < 256; i++)
            {
                double diff = i - mean;
                variance += diff * diff * bins[i];
            }
            double stdDev = Math.Sqrt(variance / total);

            // 计算 Otsu 最佳阈值
            int otsuThreshold = ComputeOtsuThreshold(bins, total);

            return new HistogramData
            {
                Bins = bins,
                Mean = Math.Round(mean, 2),
                StdDev = Math.Round(stdDev, 2),
                OtsuThreshold = otsuThreshold,
                TotalPixels = total
            };
        }

        // ==================== 2. 固定阈值二值化 ====================

        /// <summary>
        /// 固定阈值二值化。
        /// 大于阈值的像素设为255（白），小于等于阈值的设为0（黑）。
        /// </summary>
        /// <param name="image">输入灰度图</param>
        /// <param name="threshold">阈值（0~255）</param>
        /// <returns>二值化后的图像（新图像，不修改输入）</returns>
        public static ImageData Threshold(ImageData image, int threshold)
        {
            ValidateGrayscale(image);
            var result = ImageData.Create(image.Width, image.Height, 1);
            byte[] src = image.Pixels, dst = result.Pixels;
            int t = Math.Clamp(threshold, 0, 255);

            for (int i = 0; i < src.Length; i++)
                dst[i] = src[i] > t ? (byte)255 : (byte)0;

            return result;
        }

        // ==================== 3. Otsu 自动阈值二值化 ====================

        /// <summary>
        /// 大津法自动阈值二值化。
        /// 自动计算使类间方差最大的阈值，适合双峰直方图图像。
        /// </summary>
        /// <param name="image">输入灰度图</param>
        /// <returns>二值化后的图像</returns>
        public static ImageData ThresholdOtsu(ImageData image)
        {
            ValidateGrayscale(image);
            var hist = ComputeHistogram(image);
            return Threshold(image, hist.OtsuThreshold);
        }

        // ==================== 4. 均值滤波 ====================

        /// <summary>
        /// 均值滤波（盒型滤波）。
        /// 将每个像素替换为邻域内所有像素的平均值，用于去除椒盐噪声。
        /// </summary>
        /// <param name="image">输入灰度图</param>
        /// <param name="kernelSize">滤波核大小（必须为奇数，如3、5、7）</param>
        /// <returns>滤波后的图像</returns>
        public static ImageData MeanFilter(ImageData image, int kernelSize = 3)
        {
            ValidateGrayscale(image);
            if (kernelSize < 1 || kernelSize % 2 == 0)
                throw new ArgumentException($"核大小必须为正奇数，当前：{kernelSize}");

            int w = image.Width, h = image.Height;
            int half = kernelSize / 2;
            var result = ImageData.Create(w, h, 1);
            byte[] src = image.Pixels, dst = result.Pixels;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sum = 0, count = 0;
                    for (int ky = -half; ky <= half; ky++)
                    {
                        int ny = Math.Clamp(y + ky, 0, h - 1);
                        for (int kx = -half; kx <= half; kx++)
                        {
                            int nx = Math.Clamp(x + kx, 0, w - 1);
                            sum += src[ny * w + nx];
                            count++;
                        }
                    }
                    dst[y * w + x] = (byte)(sum / count);
                }
            }
            return result;
        }

        // ==================== 5. 高斯滤波 ====================

        /// <summary>
        /// 3×3 高斯滤波（σ≈0.85）。
        /// 相比均值滤波，保留更多边缘细节，常用于噪声平滑预处理。
        /// </summary>
        public static ImageData GaussianFilter3x3(ImageData image)
        {
            ValidateGrayscale(image);
            // 3×3 高斯核（总和=16，归一化）
            int[] kernel = { 1, 2, 1, 2, 4, 2, 1, 2, 1 };
            int kernelSum = 16;
            return ApplyKernel3x3(image, kernel, kernelSum);
        }

        /// <summary>
        /// 5×5 高斯滤波（σ≈1.4）。
        /// 更强的平滑效果，适合噪声较多的图像。
        /// </summary>
        public static ImageData GaussianFilter5x5(ImageData image)
        {
            ValidateGrayscale(image);
            // 5×5 高斯核（近似 σ=1.4，总和=273）
            int[] kernel = {
                1,  4,  7,  4,  1,
                4, 16, 26, 16,  4,
                7, 26, 41, 26,  7,
                4, 16, 26, 16,  4,
                1,  4,  7,  4,  1
            };
            int kernelSum = 273;
            return ApplyKernelNxN(image, kernel, 5, kernelSum);
        }

        // ==================== 6. Sobel 边缘检测 ====================

        /// <summary>
        /// Sobel 边缘检测。
        /// 分别用水平（Gx）和垂直（Gy）Sobel算子卷积，
        /// 最终梯度 G = sqrt(Gx²+Gy²)（限幅到 0~255）。
        /// </summary>
        /// <param name="image">输入灰度图（建议先高斯滤波去噪）</param>
        /// <returns>梯度幅值图像</returns>
        public static ImageData SobelEdgeDetect(ImageData image)
        {
            ValidateGrayscale(image);
            int w = image.Width, h = image.Height;
            var result = ImageData.Create(w, h, 1);
            byte[] src = image.Pixels, dst = result.Pixels;

            // Sobel 算子
            // Gx = [-1,0,+1; -2,0,+2; -1,0,+1]
            // Gy = [-1,-2,-1;  0,0, 0; +1,+2,+1]
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int p00 = src[(y - 1) * w + (x - 1)];
                    int p01 = src[(y - 1) * w + x];
                    int p02 = src[(y - 1) * w + (x + 1)];
                    int p10 = src[y * w + (x - 1)];
                    // p11 = 中心像素（Sobel算子中心系数为0，不需要）
                    int p12 = src[y * w + (x + 1)];
                    int p20 = src[(y + 1) * w + (x - 1)];
                    int p21 = src[(y + 1) * w + x];
                    int p22 = src[(y + 1) * w + (x + 1)];

                    int gx = -p00 + p02 - 2 * p10 + 2 * p12 - p20 + p22;
                    int gy = -p00 - 2 * p01 - p02 + p20 + 2 * p21 + p22;

                    // 梯度幅值（限幅）
                    int magnitude = (int)Math.Sqrt(gx * gx + gy * gy);
                    dst[y * w + x] = (byte)Math.Min(255, magnitude);
                }
            }
            return result;
        }

        // ==================== 工具方法 ====================

        /// <summary>
        /// 将彩色图像（3通道BGR）转换为灰度图（加权平均法）
        /// 灰度 = 0.299×R + 0.587×G + 0.114×B
        /// </summary>
        public static ImageData ToGrayscale(ImageData colorImage)
        {
            if (colorImage.Channels == 1) return colorImage.Clone();
            if (colorImage.Channels != 3 && colorImage.Channels != 4)
                throw new ArgumentException($"不支持的通道数：{colorImage.Channels}");

            var result = ImageData.Create(colorImage.Width, colorImage.Height, 1);
            byte[] src = colorImage.Pixels, dst = result.Pixels;
            int w = colorImage.Width, h = colorImage.Height, c = colorImage.Channels;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int offset = (y * w + x) * c;
                    // BGR 顺序
                    byte b = src[offset];
                    byte g = src[offset + 1];
                    byte r = src[offset + 2];
                    dst[y * w + x] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                }
            }
            return result;
        }

        /// <summary>
        /// 图像归一化：将像素值线性映射到 0~255
        /// </summary>
        public static ImageData Normalize(ImageData image)
        {
            ValidateGrayscale(image);
            byte min = 255, max = 0;
            foreach (var p in image.Pixels) { if (p < min) min = p; if (p > max) max = p; }

            if (max == min) return image.Clone(); // 全部相同像素，直接返回

            var result = ImageData.Create(image.Width, image.Height, 1);
            double scale = 255.0 / (max - min);
            for (int i = 0; i < image.Pixels.Length; i++)
                result.Pixels[i] = (byte)((image.Pixels[i] - min) * scale);
            return result;
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 大津法最佳阈值计算（最大化类间方差）
        /// </summary>
        private static int ComputeOtsuThreshold(int[] bins, int total)
        {
            double totalSum = 0;
            for (int i = 0; i < 256; i++) totalSum += i * bins[i];

            double sumB = 0, wB = 0, maxVar = 0;
            int bestThreshold = 0;

            for (int t = 0; t < 256; t++)
            {
                wB += bins[t];
                if (wB == 0) continue;

                double wF = total - wB;
                if (wF == 0) break;

                sumB += t * bins[t];

                double meanB = sumB / wB;                    // 背景均值
                double meanF = (totalSum - sumB) / wF;       // 前景均值

                // 类间方差
                double varBetween = wB * wF * (meanB - meanF) * (meanB - meanF);
                if (varBetween > maxVar)
                {
                    maxVar = varBetween;
                    bestThreshold = t;
                }
            }
            return bestThreshold;
        }

        /// <summary>
        /// 应用 3×3 卷积核（通用实现）
        /// </summary>
        private static ImageData ApplyKernel3x3(ImageData image, int[] kernel, int kernelSum)
        {
            int w = image.Width, h = image.Height;
            var result = ImageData.Create(w, h, 1);
            byte[] src = image.Pixels, dst = result.Pixels;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sum = 0, ki = 0;
                    for (int ky = -1; ky <= 1; ky++)
                    {
                        int ny = Math.Clamp(y + ky, 0, h - 1);
                        for (int kx = -1; kx <= 1; kx++, ki++)
                        {
                            int nx = Math.Clamp(x + kx, 0, w - 1);
                            sum += src[ny * w + nx] * kernel[ki];
                        }
                    }
                    dst[y * w + x] = (byte)Math.Clamp(sum / kernelSum, 0, 255);
                }
            }
            return result;
        }

        /// <summary>
        /// 应用 N×N 卷积核（通用实现，N为奇数）
        /// </summary>
        private static ImageData ApplyKernelNxN(ImageData image, int[] kernel, int n, int kernelSum)
        {
            int w = image.Width, h = image.Height, half = n / 2;
            var result = ImageData.Create(w, h, 1);
            byte[] src = image.Pixels, dst = result.Pixels;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sum = 0, ki = 0;
                    for (int ky = -half; ky <= half; ky++)
                    {
                        int ny = Math.Clamp(y + ky, 0, h - 1);
                        for (int kx = -half; kx <= half; kx++, ki++)
                        {
                            int nx = Math.Clamp(x + kx, 0, w - 1);
                            sum += src[ny * w + nx] * kernel[ki];
                        }
                    }
                    dst[y * w + x] = (byte)Math.Clamp(sum / kernelSum, 0, 255);
                }
            }
            return result;
        }

        /// <summary>
        /// 验证图像为灰度图（单通道）
        /// </summary>
        private static void ValidateGrayscale(ImageData image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (image.Channels != 1) throw new ArgumentException($"此算法仅支持灰度图（1通道），当前：{image.Channels}通道");
            if (image.Pixels == null || image.Pixels.Length == 0) throw new ArgumentException("图像像素数据为空");
        }
    }
}
