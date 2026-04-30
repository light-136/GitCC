// ============================================================
// 文件：MorphologyProcessor.cs
// 用途：形态学运算 — 腐蚀/膨胀/开运算/闭运算/顶帽/黑帽
// 设计思路：
//   形态学运算是图像处理的基础工具，用于：
//   - 腐蚀(Erode)：缩小白色区域，去除小白点噪声
//   - 膨胀(Dilate)：扩大白色区域，填充小黑洞
//   - 开运算(Open)：先腐蚀后膨胀，去噪声保大结构
//   - 闭运算(Close)：先膨胀后腐蚀，填空洞保大结构
//   - 顶帽(TopHat)：原图 - 开运算，提取亮特征
//   - 黑帽(BlackHat)：闭运算 - 原图，提取暗特征
//
//   所有运算基于结构元素（矩形核），支持自定义核大小。
// ============================================================

using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Modules.Vision
{
    /// <summary>
    /// 形态学操作类型。
    /// </summary>
    public enum MorphologyOperation
    {
        Erode,     // 腐蚀
        Dilate,    // 膨胀
        Open,      // 开运算 = 腐蚀 + 膨胀
        Close,     // 闭运算 = 膨胀 + 腐蚀
        TopHat,    // 顶帽 = 原图 - 开运算
        BlackHat   // 黑帽 = 闭运算 - 原图
    }

    /// <summary>
    /// 形态学处理器 — 对二值或灰度图像执行形态学运算。
    ///
    /// 核心原理（以3×3矩形核为例）：
    ///   腐蚀：输出像素 = 核覆盖区域的最小值（白色缩小）
    ///   膨胀：输出像素 = 核覆盖区域的最大值（白色扩大）
    /// </summary>
    public class MorphologyProcessor : IImageProcessor
    {
        public string Name => $"形态学-{Operation}";

        /// <summary>操作类型。</summary>
        public MorphologyOperation Operation { get; set; } = MorphologyOperation.Erode;

        /// <summary>核大小（必须为奇数，默认3）。</summary>
        public int KernelSize { get; set; } = 3;

        /// <summary>迭代次数（默认1）。</summary>
        public int Iterations { get; set; } = 1;

        public ImageData Process(ImageData input, Dictionary<string, object>? parameters = null)
        {
            return Operation switch
            {
                MorphologyOperation.Erode => ApplyIterative(input, ErodeOnce, Iterations),
                MorphologyOperation.Dilate => ApplyIterative(input, DilateOnce, Iterations),
                MorphologyOperation.Open => ApplyOpen(input),
                MorphologyOperation.Close => ApplyClose(input),
                MorphologyOperation.TopHat => ApplyTopHat(input),
                MorphologyOperation.BlackHat => ApplyBlackHat(input),
                _ => input.Clone()
            };
        }

        /// <summary>
        /// 腐蚀 — 取邻域最小值。
        /// 白色区域缩小，小白点消失。
        /// </summary>
        private ImageData ErodeOnce(ImageData input)
        {
            int w = input.Width, h = input.Height;
            int radius = KernelSize / 2;
            var output = ImageData.Create(w, h, 1);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    byte min = 255;
                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int sy = Math.Clamp(y + ky, 0, h - 1);
                            int sx = Math.Clamp(x + kx, 0, w - 1);
                            byte val = input.Pixels[sy * input.Stride + sx * input.Channels];
                            if (val < min) min = val;
                        }
                    }
                    output.Pixels[y * output.Stride + x] = min;
                }
            }

            return output;
        }

        /// <summary>
        /// 膨胀 — 取邻域最大值。
        /// 白色区域扩大，小黑洞填充。
        /// </summary>
        private ImageData DilateOnce(ImageData input)
        {
            int w = input.Width, h = input.Height;
            int radius = KernelSize / 2;
            var output = ImageData.Create(w, h, 1);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    byte max = 0;
                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int sy = Math.Clamp(y + ky, 0, h - 1);
                            int sx = Math.Clamp(x + kx, 0, w - 1);
                            byte val = input.Pixels[sy * input.Stride + sx * input.Channels];
                            if (val > max) max = val;
                        }
                    }
                    output.Pixels[y * output.Stride + x] = max;
                }
            }

            return output;
        }

        /// <summary>开运算 = 腐蚀 + 膨胀。去除小白点噪声，保持大结构。</summary>
        private ImageData ApplyOpen(ImageData input)
        {
            var eroded = ApplyIterative(input, ErodeOnce, Iterations);
            return ApplyIterative(eroded, DilateOnce, Iterations);
        }

        /// <summary>闭运算 = 膨胀 + 腐蚀。填充小黑洞，保持大结构。</summary>
        private ImageData ApplyClose(ImageData input)
        {
            var dilated = ApplyIterative(input, DilateOnce, Iterations);
            return ApplyIterative(dilated, ErodeOnce, Iterations);
        }

        /// <summary>顶帽 = 原图 - 开运算。提取比背景亮的小特征。</summary>
        private ImageData ApplyTopHat(ImageData input)
        {
            var opened = ApplyOpen(input);
            return SubtractImages(input, opened);
        }

        /// <summary>黑帽 = 闭运算 - 原图。提取比背景暗的小特征。</summary>
        private ImageData ApplyBlackHat(ImageData input)
        {
            var closed = ApplyClose(input);
            return SubtractImages(closed, input);
        }

        /// <summary>迭代应用某个操作。</summary>
        private static ImageData ApplyIterative(ImageData input, Func<ImageData, ImageData> op, int count)
        {
            var current = input;
            for (int i = 0; i < count; i++)
                current = op(current);
            return current;
        }

        /// <summary>图像相减（像素级），结果钳位到[0,255]。</summary>
        private static ImageData SubtractImages(ImageData a, ImageData b)
        {
            var output = ImageData.Create(a.Width, a.Height, 1);
            int total = a.Width * a.Height;
            for (int i = 0; i < total; i++)
            {
                int diff = a.Pixels[i * a.Channels] - b.Pixels[i * b.Channels];
                output.Pixels[i] = (byte)Math.Clamp(diff, 0, 255);
            }
            return output;
        }
    }
}
