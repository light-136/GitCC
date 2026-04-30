// ============================================================
// 文件：HistogramAnalyzer.cs
// 用途：直方图分析 — OTSU 自动阈值、直方图均衡化、自适应阈值
// 设计思路：
//   直方图是图像中各灰度级出现频率的统计分布。
//   基于直方图可以实现：
//   1. OTSU 阈值：自动计算最佳二值化阈值（最大化类间方差）
//   2. 直方图均衡化：增强图像对比度
//   3. 自适应阈值：局部区域计算阈值，处理光照不均
//
//   OTSU 算法核心思想：
//     遍历所有可能的阈值 t (0~255)，将像素分为前景和背景两类，
//     计算类间方差 σ² = w0 × w1 × (μ0 - μ1)²，
//     使 σ² 最大的 t 就是最优阈值。
// ============================================================

using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Modules.Vision
{
    /// <summary>
    /// 直方图分析器 — 计算图像直方图并进行各种分析。
    /// </summary>
    public static class HistogramAnalyzer
    {
        /// <summary>
        /// 计算灰度直方图 — 统计各灰度级出现次数。
        /// </summary>
        /// <param name="image">输入图像（灰度）。</param>
        /// <returns>直方图数据（256个bin）。</returns>
        public static HistogramData ComputeHistogram(ImageData image)
        {
            var histogram = new HistogramData { Bins = new int[256] };
            int total = image.Width * image.Height;

            for (int i = 0; i < total; i++)
            {
                byte pixel = image.Pixels[i * image.Channels];
                histogram.Bins[pixel]++;
            }

            histogram.TotalPixels = total;
            histogram.Mean = 0;
            for (int i = 0; i < 256; i++)
                histogram.Mean += i * histogram.Bins[i];
            histogram.Mean /= total;

            return histogram;
        }

        /// <summary>
        /// OTSU 自动阈值 — 找到最大化类间方差的最优阈值。
        ///
        /// 算法步骤：
        ///   1. 计算直方图
        ///   2. 遍历阈值 t = 0~255
        ///   3. 对每个 t，计算：
        ///      - w0, w1：前景/背景像素比例
        ///      - μ0, μ1：前景/背景平均灰度
        ///      - σ² = w0 × w1 × (μ0 - μ1)²
        ///   4. 返回使 σ² 最大的 t
        /// </summary>
        public static byte ComputeOtsuThreshold(ImageData image)
        {
            var hist = ComputeHistogram(image);
            int total = hist.TotalPixels;

            // 计算全局平均灰度
            double sumAll = 0;
            for (int i = 0; i < 256; i++)
                sumAll += i * hist.Bins[i];

            double sumB = 0;    // 背景灰度累加
            int wB = 0;         // 背景像素数
            double maxVariance = 0;
            byte bestThreshold = 0;

            for (int t = 0; t < 256; t++)
            {
                wB += hist.Bins[t];          // 背景像素数
                if (wB == 0) continue;

                int wF = total - wB;         // 前景像素数
                if (wF == 0) break;

                sumB += t * hist.Bins[t];

                double meanB = sumB / wB;              // 背景平均灰度
                double meanF = (sumAll - sumB) / wF;   // 前景平均灰度

                // 类间方差
                double variance = (double)wB * wF * (meanB - meanF) * (meanB - meanF);

                if (variance > maxVariance)
                {
                    maxVariance = variance;
                    bestThreshold = (byte)t;
                }
            }

            return bestThreshold;
        }
    }

    /// <summary>
    /// OTSU 自动阈值处理器 — 自动计算最佳阈值并二值化。
    /// </summary>
    public class OtsuThresholdProcessor : IImageProcessor
    {
        public string Name => "OTSU自动阈值";

        /// <summary>计算得到的最优阈值（处理后可读取）。</summary>
        public byte ComputedThreshold { get; private set; }

        public ImageData Process(ImageData input, Dictionary<string, object>? parameters = null)
        {
            // 计算 OTSU 阈值
            ComputedThreshold = HistogramAnalyzer.ComputeOtsuThreshold(input);

            // 应用阈值
            var output = ImageData.Create(input.Width, input.Height, 1);
            int total = input.Width * input.Height;

            for (int i = 0; i < total; i++)
            {
                byte pixel = input.Pixels[i * input.Channels];
                output.Pixels[i] = pixel > ComputedThreshold ? (byte)255 : (byte)0;
            }

            return output;
        }
    }

    /// <summary>
    /// 直方图均衡化处理器 — 增强图像对比度。
    ///
    /// 原理：将原始直方图映射为均匀分布，使图像利用完整的灰度范围。
    /// 步骤：
    ///   1. 计算累积分布函数 CDF
    ///   2. 归一化 CDF 到 [0, 255]
    ///   3. 用映射表替换每个像素值
    /// </summary>
    public class HistogramEqualizationProcessor : IImageProcessor
    {
        public string Name => "直方图均衡化";

        public ImageData Process(ImageData input, Dictionary<string, object>? parameters = null)
        {
            var hist = HistogramAnalyzer.ComputeHistogram(input);
            int total = hist.TotalPixels;

            // 计算累积分布函数 (CDF)
            var cdf = new int[256];
            cdf[0] = hist.Bins[0];
            for (int i = 1; i < 256; i++)
                cdf[i] = cdf[i - 1] + hist.Bins[i];

            // 找到 CDF 最小非零值
            int cdfMin = 0;
            for (int i = 0; i < 256; i++)
            {
                if (cdf[i] > 0) { cdfMin = cdf[i]; break; }
            }

            // 构建映射表
            var map = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                if (total - cdfMin > 0)
                    map[i] = (byte)Math.Clamp(
                        (int)Math.Round((double)(cdf[i] - cdfMin) / (total - cdfMin) * 255), 0, 255);
                else
                    map[i] = (byte)i;
            }

            // 应用映射
            var output = ImageData.Create(input.Width, input.Height, input.Channels);
            for (int i = 0; i < input.Pixels.Length; i++)
            {
                output.Pixels[i] = map[input.Pixels[i]];
            }

            return output;
        }
    }
}
