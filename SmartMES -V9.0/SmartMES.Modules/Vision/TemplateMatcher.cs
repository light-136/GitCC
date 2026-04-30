// ============================================================
// 文件：TemplateMatcher.cs
// 用途：模板匹配 — NCC 归一化互相关 + 图像金字塔 + NMS
// 设计思路：
//   模板匹配是工业视觉中最常用的定位算法之一。
//   本实现采用 NCC（归一化互相关系数）作为匹配度量，
//   结合图像金字塔加速搜索和 NMS（非极大值抑制）过滤重叠匹配。
//
//   NCC 公式：
//     NCC(x,y) = Σ[(I(x+i,y+j) - Ī) × (T(i,j) - T̄)]
//                ÷ sqrt[Σ(I-Ī)² × Σ(T-T̄)²]
//   其中 I=图像区域, T=模板, Ī/T̄=平均值
//   NCC 范围 [-1, 1]，1 表示完全匹配
//
//   搜索加速：
//     1. 构建图像金字塔（每层缩小2倍）
//     2. 在最小层粗搜索候选位置
//     3. 逐层精细搜索（在上层候选位置的邻域）
//     4. NMS 过滤重叠结果
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Modules.Vision
{
    /// <summary>
    /// 模板匹配器 — NCC + 金字塔 + NMS 实现高效模板定位。
    /// </summary>
    public class TemplateMatcher
    {
        /// <summary>匹配得分阈值（0~1，默认0.7）。</summary>
        public double ScoreThreshold { get; set; } = 0.7;

        /// <summary>最大匹配数。</summary>
        public int MaxMatches { get; set; } = 10;

        /// <summary>金字塔层数（0=不使用金字塔）。</summary>
        public int PyramidLevels { get; set; } = 2;

        /// <summary>NMS 重叠阈值（IoU > 此值则抑制）。</summary>
        public double NmsThreshold { get; set; } = 0.3;

        /// <summary>
        /// 执行模板匹配 — 在图像中搜索模板的所有实例。
        /// </summary>
        /// <param name="image">搜索图像（灰度）。</param>
        /// <param name="template">模板图像（灰度）。</param>
        /// <returns>匹配结果列表，按得分降序排列。</returns>
        public List<TemplateMatchResult> Match(ImageData image, ImageData template)
        {
            List<TemplateMatchResult> candidates;

            if (PyramidLevels > 0)
            {
                // 金字塔加速搜索
                candidates = PyramidSearch(image, template);
            }
            else
            {
                // 全图暴力搜索
                candidates = BruteForceSearch(image, template);
            }

            // NMS 过滤重叠
            var filtered = ApplyNms(candidates, template.Width, template.Height);

            // 按得分排序，限制数量
            return filtered
                .OrderByDescending(r => r.Score)
                .Take(MaxMatches)
                .ToList();
        }

        /// <summary>
        /// NCC 暴力搜索 — 遍历所有位置计算 NCC 得分。
        /// 时间复杂度 O(W×H×tw×th)，适合小模板或底层金字塔。
        /// </summary>
        private List<TemplateMatchResult> BruteForceSearch(ImageData image, ImageData template)
            => BruteForceSearch(image, template, ScoreThreshold);

        /// <summary>
        /// 暴力搜索（指定阈值版本）— 用于金字塔搜索顶层的放宽阈值场景。
        /// </summary>
        private List<TemplateMatchResult> BruteForceSearch(ImageData image, ImageData template, double threshold)
        {
            var results = new List<TemplateMatchResult>();
            int tw = template.Width, th = template.Height;
            int sw = image.Width - tw, sh = image.Height - th;

            // 预计算模板均值和标准差
            double tMean = ComputeMean(template, 0, 0, tw, th);
            double tStdDev = ComputeStdDev(template, 0, 0, tw, th, tMean);
            if (tStdDev < 1e-6) return results;

            for (int y = 0; y <= sh; y++)
            {
                for (int x = 0; x <= sw; x++)
                {
                    double score = ComputeNcc(image, template, x, y, tMean, tStdDev);

                    if (score >= threshold)
                    {
                        results.Add(new TemplateMatchResult
                        {
                            X = (int)(x + tw / 2.0),
                            Y = (int)(y + th / 2.0),
                            Score = score
                        });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 金字塔搜索 — 在多层图像金字塔上由粗到精搜索。
        /// </summary>
        private List<TemplateMatchResult> PyramidSearch(ImageData image, ImageData template)
        {
            // 构建金字塔
            var imagePyramid = BuildPyramid(image, PyramidLevels);
            var templatePyramid = BuildPyramid(template, PyramidLevels);

            // 在最顶层（最小层）全搜索
            int topLevel = imagePyramid.Count - 1;
            var topImage = imagePyramid[topLevel];
            var topTemplate = templatePyramid[topLevel];

            // 顶层使用放宽的阈值搜索（用局部变量保证线程安全）
            double originalThreshold = ScoreThreshold;
            double topLevelThreshold = Math.Max(0.3, originalThreshold - 0.2);
            var candidates = BruteForceSearch(topImage, topTemplate, topLevelThreshold);

            // 逐层精细搜索
            for (int level = topLevel - 1; level >= 0; level--)
            {
                var levelImage = imagePyramid[level];
                var levelTemplate = templatePyramid[level];
                var refined = new List<TemplateMatchResult>();

                double tMean = ComputeMean(levelTemplate, 0, 0, levelTemplate.Width, levelTemplate.Height);
                double tStdDev = ComputeStdDev(levelTemplate, 0, 0,
                    levelTemplate.Width, levelTemplate.Height, tMean);

                foreach (var c in candidates)
                {
                    // 上层候选位置映射到当前层（×2）
                    int cx = (int)(c.X * 2 - levelTemplate.Width / 2.0);
                    int cy = (int)(c.Y * 2 - levelTemplate.Height / 2.0);
                    int searchRadius = 4;

                    double bestScore = -1;
                    int bestX = cx, bestY = cy;

                    for (int dy = -searchRadius; dy <= searchRadius; dy++)
                    {
                        for (int dx = -searchRadius; dx <= searchRadius; dx++)
                        {
                            int sx = cx + dx, sy = cy + dy;
                            if (sx < 0 || sy < 0 ||
                                sx + levelTemplate.Width > levelImage.Width ||
                                sy + levelTemplate.Height > levelImage.Height)
                                continue;

                            double score = ComputeNcc(levelImage, levelTemplate,
                                sx, sy, tMean, tStdDev);

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestX = sx;
                                bestY = sy;
                            }
                        }
                    }

                    if (bestScore >= ScoreThreshold)
                    {
                        refined.Add(new TemplateMatchResult
                        {
                            X = (int)(bestX + levelTemplate.Width / 2.0),
                            Y = (int)(bestY + levelTemplate.Height / 2.0),
                            Score = bestScore
                        });
                    }
                }

                candidates = refined;
            }

            return candidates;
        }

        /// <summary>
        /// 计算 NCC 得分 — 归一化互相关系数。
        /// </summary>
        private static double ComputeNcc(ImageData image, ImageData template,
                                          int ox, int oy, double tMean, double tStdDev)
        {
            int tw = template.Width, th = template.Height;
            double iMean = ComputeMean(image, ox, oy, tw, th);

            double crossCorr = 0;
            double iVar = 0;

            for (int j = 0; j < th; j++)
            {
                for (int i = 0; i < tw; i++)
                {
                    double iv = image.Pixels[(oy + j) * image.Stride + (ox + i) * image.Channels] - iMean;
                    double tv = template.Pixels[j * template.Stride + i * template.Channels] - tMean;
                    crossCorr += iv * tv;
                    iVar += iv * iv;
                }
            }

            double tSumVar = tStdDev * tStdDev * tw * th; // Σ(T-T̄)²
            double denom = Math.Sqrt(iVar * tSumVar);
            return denom > 1e-6 ? crossCorr / denom : 0;
        }

        /// <summary>计算图像子区域的平均灰度。</summary>
        private static double ComputeMean(ImageData img, int ox, int oy, int w, int h)
        {
            double sum = 0;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                    sum += img.Pixels[(oy + j) * img.Stride + (ox + i) * img.Channels];
            return sum / (w * h);
        }

        /// <summary>计算图像子区域的标准差。</summary>
        private static double ComputeStdDev(ImageData img, int ox, int oy, int w, int h, double mean)
        {
            double sum = 0;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    double d = img.Pixels[(oy + j) * img.Stride + (ox + i) * img.Channels] - mean;
                    sum += d * d;
                }
            return Math.Sqrt(sum / (w * h));
        }

        /// <summary>
        /// 构建图像金字塔 — 每层缩小2倍（双线性插值下采样）。
        /// </summary>
        private static List<ImageData> BuildPyramid(ImageData image, int levels)
        {
            var pyramid = new List<ImageData> { image };
            var current = image;

            for (int i = 0; i < levels; i++)
            {
                int nw = Math.Max(current.Width / 2, 1);
                int nh = Math.Max(current.Height / 2, 1);
                var down = ImageData.Create(nw, nh, 1);

                for (int y = 0; y < nh; y++)
                {
                    for (int x = 0; x < nw; x++)
                    {
                        int sx = x * 2, sy = y * 2;
                        int sum = current.Pixels[sy * current.Stride + sx * current.Channels];
                        int count = 1;
                        if (sx + 1 < current.Width) { sum += current.Pixels[sy * current.Stride + (sx + 1) * current.Channels]; count++; }
                        if (sy + 1 < current.Height) { sum += current.Pixels[(sy + 1) * current.Stride + sx * current.Channels]; count++; }
                        if (sx + 1 < current.Width && sy + 1 < current.Height) { sum += current.Pixels[(sy + 1) * current.Stride + (sx + 1) * current.Channels]; count++; }
                        down.Pixels[y * down.Stride + x] = (byte)(sum / count);
                    }
                }

                pyramid.Add(down);
                current = down;
            }

            return pyramid;
        }

        /// <summary>
        /// NMS（非极大值抑制）— 过滤重叠的匹配结果。
        /// 按得分降序排列，对每个候选检查是否与已选中的候选重叠过多。
        /// </summary>
        private List<TemplateMatchResult> ApplyNms(List<TemplateMatchResult> candidates, int tw, int th)
        {
            var sorted = candidates.OrderByDescending(c => c.Score).ToList();
            var selected = new List<TemplateMatchResult>();

            foreach (var c in sorted)
            {
                bool overlaps = false;
                foreach (var s in selected)
                {
                    double iou = ComputeIou(c, s, tw, th);
                    if (iou > NmsThreshold)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps)
                    selected.Add(c);
            }

            return selected;
        }

        /// <summary>计算两个匹配结果的 IoU（交并比）。</summary>
        private static double ComputeIou(TemplateMatchResult a, TemplateMatchResult b, int tw, int th)
        {
            double ax1 = a.X - tw / 2.0, ay1 = a.Y - th / 2.0;
            double bx1 = b.X - tw / 2.0, by1 = b.Y - th / 2.0;

            double interX = Math.Max(0, Math.Min(ax1 + tw, bx1 + tw) - Math.Max(ax1, bx1));
            double interY = Math.Max(0, Math.Min(ay1 + th, by1 + th) - Math.Max(ay1, by1));
            double interArea = interX * interY;
            double unionArea = 2.0 * tw * th - interArea;

            return unionArea > 0 ? interArea / unionArea : 0;
        }
    }
}
