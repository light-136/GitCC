// ============================================================
// 文件：BlobAnalyzer.cs
// 用途：Blob 分析 — 连通域标记(CCL) + 特征提取
// 设计思路：
//   Blob 分析用于在二值图像中识别和度量独立的连通区域（"斑点"）。
//   本实现采用两遍扫描法 + 并查集（Union-Find）：
//
//   第一遍扫描：
//     从左到右、从上到下扫描每个白色像素：
//     - 检查左邻和上邻的标签
//     - 如果都无标签，分配新标签
//     - 如果有一个有标签，继承那个标签
//     - 如果两个都有标签且不同，取较小标签，并在并查集中合并
//
//   第二遍扫描：
//     将所有标签替换为并查集中的根标签（统一标签）
//
//   特征提取：
//     对每个标记区域计算面积、中心、周长、圆度、伸长率等特征
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Modules.Vision
{
    /// <summary>
    /// Blob 分析器 — 连通域标记与特征提取。
    /// </summary>
    public class BlobAnalyzer
    {
        /// <summary>最小面积阈值（像素数，低于此值的 Blob 被过滤）。</summary>
        public int MinArea { get; set; } = 10;

        /// <summary>最大面积阈值（像素数，高于此值的 Blob 被过滤）。</summary>
        public int MaxArea { get; set; } = int.MaxValue;

        /// <summary>
        /// 执行 Blob 分析 — 在二值图像中查找并度量所有连通域。
        /// </summary>
        /// <param name="binaryImage">输入二值图像（0=背景, 255=前景）。</param>
        /// <returns>Blob 信息列表。</returns>
        public List<BlobInfo> Analyze(ImageData binaryImage)
        {
            int w = binaryImage.Width, h = binaryImage.Height;

            // 第一遍 + 第二遍：连通域标记
            var labels = LabelConnectedComponents(binaryImage);

            // 特征提取
            return ExtractFeatures(binaryImage, labels, w, h);
        }

        /// <summary>
        /// 两遍扫描连通域标记算法（4-连通）。
        ///
        /// 第一遍：分配初始标签，记录标签等价关系
        /// 第二遍：用并查集统一所有等价标签
        /// </summary>
        private int[,] LabelConnectedComponents(ImageData image)
        {
            int w = image.Width, h = image.Height;
            var labels = new int[h, w];
            int nextLabel = 1;

            // 并查集
            var parent = new Dictionary<int, int>();

            // 查找根（带路径压缩）
            int Find(int x)
            {
                while (parent.ContainsKey(x) && parent[x] != x)
                {
                    parent[x] = parent.GetValueOrDefault(parent[x], parent[x]);
                    x = parent[x];
                }
                return x;
            }

            // 合并两个标签集合
            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb)
                {
                    int min = Math.Min(ra, rb);
                    int max = Math.Max(ra, rb);
                    parent[max] = min;
                }
            }

            // ===== 第一遍扫描 =====
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 只处理前景像素（值 > 0）
                    if (image.Pixels[y * image.Stride + x * image.Channels] == 0)
                    {
                        labels[y, x] = 0;
                        continue;
                    }

                    int left = (x > 0) ? labels[y, x - 1] : 0;
                    int up = (y > 0) ? labels[y - 1, x] : 0;

                    if (left == 0 && up == 0)
                    {
                        // 无邻居标签 → 分配新标签
                        labels[y, x] = nextLabel;
                        parent[nextLabel] = nextLabel;
                        nextLabel++;
                    }
                    else if (left > 0 && up == 0)
                    {
                        labels[y, x] = left;
                    }
                    else if (left == 0 && up > 0)
                    {
                        labels[y, x] = up;
                    }
                    else
                    {
                        // 两个邻居都有标签
                        labels[y, x] = Math.Min(left, up);
                        if (left != up)
                        {
                            Union(left, up); // 记录等价关系
                        }
                    }
                }
            }

            // ===== 第二遍扫描：统一标签 =====
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (labels[y, x] > 0)
                    {
                        labels[y, x] = Find(labels[y, x]);
                    }
                }
            }

            return labels;
        }

        /// <summary>
        /// 提取每个连通域的特征。
        /// </summary>
        private List<BlobInfo> ExtractFeatures(ImageData image, int[,] labels, int w, int h)
        {
            // 按标签分组统计
            var blobData = new Dictionary<int, (int area, long sumX, long sumY,
                int minX, int minY, int maxX, int maxY, int perimeter)>();

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int label = labels[y, x];
                    if (label == 0) continue;

                    if (!blobData.ContainsKey(label))
                    {
                        blobData[label] = (0, 0, 0, x, y, x, y, 0);
                    }

                    var d = blobData[label];

                    // 检查是否为边界像素（4-连通邻域中有背景像素）
                    bool isBorder = x == 0 || y == 0 || x == w - 1 || y == h - 1 ||
                                    labels[y, x - 1] == 0 || labels[y, x + 1 < w ? x + 1 : x] == 0 ||
                                    labels[y - 1, x] == 0 || labels[y + 1 < h ? y + 1 : y, x] == 0;

                    blobData[label] = (
                        d.area + 1,
                        d.sumX + x,
                        d.sumY + y,
                        Math.Min(d.minX, x),
                        Math.Min(d.minY, y),
                        Math.Max(d.maxX, x),
                        Math.Max(d.maxY, y),
                        d.perimeter + (isBorder ? 1 : 0)
                    );
                }
            }

            // 构建 BlobInfo 列表
            var blobs = new List<BlobInfo>();
            foreach (var (label, d) in blobData)
            {
                if (d.area < MinArea || d.area > MaxArea)
                    continue;

                double centerX = (double)d.sumX / d.area;
                double centerY = (double)d.sumY / d.area;
                double bboxWidth = d.maxX - d.minX + 1;
                double bboxHeight = d.maxY - d.minY + 1;

                // 圆度 = 4π × 面积 / 周长²
                double circularity = d.perimeter > 0
                    ? 4 * Math.PI * d.area / (d.perimeter * d.perimeter)
                    : 0;

                // 伸长率 = 长边 / 短边
                double elongation = Math.Min(bboxWidth, bboxHeight) > 0
                    ? Math.Max(bboxWidth, bboxHeight) / Math.Min(bboxWidth, bboxHeight)
                    : 1;

                blobs.Add(new BlobInfo
                {
                    Area = d.area,
                    CenterX = centerX,
                    CenterY = centerY,
                    Perimeter = d.perimeter,
                    Circularity = circularity,
                    Elongation = elongation,
                    BoundX = d.minX,
                    BoundY = d.minY,
                    BoundWidth = (int)bboxWidth,
                    BoundHeight = (int)bboxHeight
                });
            }

            return blobs.OrderByDescending(b => b.Area).ToList();
        }

        /// <summary>
        /// 获取标记图像（可视化用） — 每个 Blob 用不同灰度值表示。
        /// </summary>
        public ImageData GetLabeledImage(ImageData binaryImage)
        {
            int w = binaryImage.Width, h = binaryImage.Height;
            var labels = LabelConnectedComponents(binaryImage);

            // 找到最大标签值
            int maxLabel = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    maxLabel = Math.Max(maxLabel, labels[y, x]);

            var output = ImageData.Create(w, h, 1);
            if (maxLabel == 0) return output;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (labels[y, x] > 0)
                    {
                        // 映射标签到灰度值（避免0和255）
                        output.Pixels[y * w + x] = (byte)(30 + (labels[y, x] * 200 / maxLabel) % 225);
                    }
                }
            }

            return output;
        }
    }
}
