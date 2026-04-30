using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartMES.Modules.Vision
{
    public enum DetectionResult { OK, NG, Unknown }

    public class DefectInfo
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Type { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    public class InspectionResult
    {
        public DetectionResult Result { get; set; }
        public List<DefectInfo> Defects { get; set; } = new();
        public double Score { get; set; }
        public string Message { get; set; } = string.Empty;
        public TimeSpan ProcessTime { get; set; }
        public WriteableBitmap? ProcessedImage { get; set; }
    }

    /// <summary>视觉处理引擎（纯 C# 示例）。</summary>
    public static class VisionEngine
    {
        /// <summary>生成模拟工件图像。</summary>
        public static WriteableBitmap GenerateWorkpieceImage(int width = 640, int height = 480, bool hasDefect = false)
        {
            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            var pixels = new int[width * height];

            for (int i = 0; i < pixels.Length; i++)
            {
                int noise = Random.Shared.Next(20, 45);
                pixels[i] = (noise << 16) | (noise << 8) | noise;
            }

            int mx = width / 6, my = height / 6;
            int mw = width * 4 / 6, mh = height * 4 / 6;
            for (int y = my; y < my + mh; y++)
            {
                for (int x = mx; x < mx + mw; x++)
                {
                    int v = 160 + Random.Shared.Next(-8, 8);
                    pixels[y * width + x] = (v << 16) | (v << 8) | v;
                }
            }

            if (hasDefect)
            {
                int numDefects = Random.Shared.Next(1, 4);
                for (int d = 0; d < numDefects; d++)
                {
                    int dx = mx + Random.Shared.Next(20, mw - 40);
                    int dy = my + Random.Shared.Next(20, mh - 40);
                    int dw = Random.Shared.Next(8, 30);
                    int dh = Random.Shared.Next(8, 20);
                    for (int y = dy; y < Math.Min(dy + dh, my + mh); y++)
                    {
                        for (int x = dx; x < Math.Min(dx + dw, mx + mw); x++)
                        {
                            int v = Random.Shared.Next(20, 60);
                            pixels[y * width + x] = (v << 16) | (v << 8) | v;
                        }
                    }
                }
            }

            wb.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            return wb;
        }

        /// <summary>灰度化。</summary>
        public static byte[] ToGrayscale(WriteableBitmap src)
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            var pixels = new int[w * h];
            src.CopyPixels(pixels, w * 4, 0);
            var gray = new byte[w * h];
            for (int i = 0; i < pixels.Length; i++)
            {
                int b = pixels[i] & 0xFF;
                int g = (pixels[i] >> 8) & 0xFF;
                int r = (pixels[i] >> 16) & 0xFF;
                gray[i] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            }
            return gray;
        }

        /// <summary>Sobel 边缘检测。</summary>
        public static WriteableBitmap SobelEdge(WriteableBitmap src)
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            var gray = ToGrayscale(src);
            var result = new int[w * h];

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int gx = -gray[(y - 1) * w + (x - 1)] + gray[(y - 1) * w + (x + 1)]
                             - 2 * gray[y * w + (x - 1)] + 2 * gray[y * w + (x + 1)]
                             - gray[(y + 1) * w + (x - 1)] + gray[(y + 1) * w + (x + 1)];
                    int gy = -gray[(y - 1) * w + (x - 1)] - 2 * gray[(y - 1) * w + x] - gray[(y - 1) * w + (x + 1)]
                             + gray[(y + 1) * w + (x - 1)] + 2 * gray[(y + 1) * w + x] + gray[(y + 1) * w + (x + 1)];
                    int mag = Math.Min(255, (int)Math.Sqrt(gx * gx + gy * gy));
                    result[y * w + x] = (mag << 16) | (mag << 8) | mag;
                }
            }

            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), result, w * 4, 0);
            return wb;
        }

        /// <summary>缺陷检测（阈值法）。</summary>
        public static InspectionResult Inspect(WriteableBitmap src, int threshold = 50, double defectRatioLimit = 0.005)
        {
            var start = DateTime.Now;
            int w = src.PixelWidth, h = src.PixelHeight;
            var gray = ToGrayscale(src);

            int mx = w / 6, my = h / 6, mw = w * 4 / 6, mh = h * 4 / 6;
            int totalPx = mw * mh;
            int darkPx = 0;
            var defects = new List<DefectInfo>();

            int defectX = -1, defectY = -1, defectW = 0, defectH = 0;
            for (int y = my; y < my + mh; y++)
            {
                bool rowHasDark = false;
                int rowStart = -1;
                for (int x = mx; x < mx + mw; x++)
                {
                    if (gray[y * w + x] < threshold)
                    {
                        darkPx++;
                        rowHasDark = true;
                        if (rowStart < 0) rowStart = x;
                        defectW = Math.Max(defectW, x - rowStart + 1);
                        if (defectX < 0) { defectX = x; defectY = y; }
                    }
                }
                if (rowHasDark) defectH++;
            }

            double defectRatio = (double)darkPx / totalPx;
            bool isNG = defectRatio > defectRatioLimit;

            if (isNG && defectX > 0)
            {
                defects.Add(new DefectInfo
                {
                    X = defectX,
                    Y = defectY,
                    Width = Math.Max(10, defectW),
                    Height = Math.Max(8, defectH),
                    Type = defectRatio > 0.02 ? "大面积污渍" : "划伤/暗斑",
                    Confidence = Math.Min(0.99, defectRatio * 30)
                });
            }

            var annotated = DrawAnnotations(src, defects, isNG);
            var score = Math.Max(0, Math.Round((1.0 - defectRatio * 100) * 100, 1));

            return new InspectionResult
            {
                Result = isNG ? DetectionResult.NG : DetectionResult.OK,
                Defects = defects,
                Score = score,
                Message = isNG
                    ? $"NG - 检测到 {defects.Count} 处缺陷，暗像素率: {defectRatio:P2}"
                    : $"OK - 产品合格，评分: {score}",
                ProcessTime = DateTime.Now - start,
                ProcessedImage = annotated
            };
        }

        /// <summary>绘制标注。</summary>
        private static WriteableBitmap DrawAnnotations(WriteableBitmap src, List<DefectInfo> defects, bool isNG)
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            var pixels = new int[w * h];
            src.CopyPixels(pixels, w * 4, 0);

            int mx = w / 6, my = h / 6, mw = w * 4 / 6, mh = h * 4 / 6;
            int borderColor = isNG ? unchecked((int)0x00FF4757) : 0x0039D353;
            DrawRect(pixels, w, mx, my, mw, mh, borderColor, 2);

            foreach (var d in defects)
                DrawRect(pixels, w, d.X, d.Y, d.Width, d.Height, 0x00FFA502, 2);

            var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
            wb.WritePixels(new System.Windows.Int32Rect(0, 0, w, h), pixels, w * 4, 0);
            return wb;
        }

        /// <summary>绘制矩形框。</summary>
        private static void DrawRect(int[] pixels, int w, int x, int y, int rw, int rh, int color, int thickness)
        {
            for (int t = 0; t < thickness; t++)
            {
                for (int i = x; i < x + rw; i++)
                {
                    SafeSet(pixels, w, i, y + t, color);
                    SafeSet(pixels, w, i, y + rh - 1 - t, color);
                }

                for (int j = y; j < y + rh; j++)
                {
                    SafeSet(pixels, w, x + t, j, color);
                    SafeSet(pixels, w, x + rw - 1 - t, j, color);
                }
            }
        }

        /// <summary>安全写像素。</summary>
        private static void SafeSet(int[] pixels, int w, int x, int y, int color)
        {
            int h = pixels.Length / w;
            if (x >= 0 && x < w && y >= 0 && y < h)
                pixels[y * w + x] = color;
        }
    }
}
