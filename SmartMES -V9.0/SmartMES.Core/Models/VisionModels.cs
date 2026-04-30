// ============================================================
// 文件：VisionModels.cs
// 用途：视觉系统领域模型 — 纯数据结构，不依赖 WPF
// 设计思路：
//   使用 ImageData（字节数组）替代 WriteableBitmap，
//   使视觉算法可以在无 WPF 环境下运行和测试。
//   WPF 转换桥接放在 Modules 层。
// ============================================================

namespace SmartMES.Core.Models
{
    // ======================== 图像数据 ========================

    /// <summary>
    /// 图像数据 — 纯字节数组表示，不依赖任何 UI 框架。
    /// Channels=1 表示灰度图，Channels=3 表示 BGR 彩色图，Channels=4 表示 BGRA。
    /// 像素按行优先存储：pixels[y * Width * Channels + x * Channels + c]。
    /// </summary>
    public class ImageData
    {
        /// <summary>像素数据（字节数组）。</summary>
        public byte[] Pixels { get; set; } = Array.Empty<byte>();

        /// <summary>图像宽度（像素）。</summary>
        public int Width { get; set; }

        /// <summary>图像高度（像素）。</summary>
        public int Height { get; set; }

        /// <summary>通道数（1=灰度，3=BGR，4=BGRA）。</summary>
        public int Channels { get; set; } = 1;

        /// <summary>每行字节数（= Width × Channels）。</summary>
        public int Stride => Width * Channels;

        /// <summary>
        /// 创建指定尺寸的空白图像。
        /// </summary>
        public static ImageData Create(int width, int height, int channels = 1)
        {
            return new ImageData
            {
                Width = width,
                Height = height,
                Channels = channels,
                Pixels = new byte[width * height * channels]
            };
        }

        /// <summary>
        /// 深拷贝当前图像。
        /// </summary>
        public ImageData Clone()
        {
            return new ImageData
            {
                Width = Width,
                Height = Height,
                Channels = Channels,
                Pixels = (byte[])Pixels.Clone()
            };
        }

        /// <summary>
        /// 获取指定位置的像素值（灰度图返回单值，彩色图返回第一通道）。
        /// </summary>
        public byte GetPixel(int x, int y)
        {
            return Pixels[y * Stride + x * Channels];
        }

        /// <summary>
        /// 设置指定位置的像素值。
        /// </summary>
        public void SetPixel(int x, int y, byte value)
        {
            int offset = y * Stride + x * Channels;
            for (int c = 0; c < Channels; c++)
                Pixels[offset + c] = value;
        }
    }

    // ======================== 模板匹配结果 ========================

    /// <summary>
    /// 模板匹配结果 — 描述在图像中找到的一个匹配位置。
    /// </summary>
    public class TemplateMatchResult
    {
        /// <summary>匹配得分（0~1，1 为完美匹配）。</summary>
        public double Score { get; set; }

        /// <summary>匹配位置 X 坐标（像素）。</summary>
        public int X { get; set; }

        /// <summary>匹配位置 Y 坐标（像素）。</summary>
        public int Y { get; set; }

        /// <summary>匹配角度（度），用于旋转不变匹配。</summary>
        public double Angle { get; set; }
    }

    // ======================== Blob 分析结果 ========================

    /// <summary>
    /// Blob 信息 — 连通域分析的单个区域特征。
    /// </summary>
    public class BlobInfo
    {
        /// <summary>Blob 标签 ID。</summary>
        public int Id { get; set; }

        /// <summary>面积（像素数）。</summary>
        public int Area { get; set; }

        /// <summary>质心 X 坐标。</summary>
        public double CenterX { get; set; }

        /// <summary>质心 Y 坐标。</summary>
        public double CenterY { get; set; }

        /// <summary>外接矩形左上角 X。</summary>
        public int BoundX { get; set; }

        /// <summary>外接矩形左上角 Y。</summary>
        public int BoundY { get; set; }

        /// <summary>外接矩形宽度。</summary>
        public int BoundWidth { get; set; }

        /// <summary>外接矩形高度。</summary>
        public int BoundHeight { get; set; }

        /// <summary>圆度（4π×面积/周长²），1.0 为完美圆形。</summary>
        public double Circularity { get; set; }

        /// <summary>伸长率（长轴/短轴比），1.0 为正圆。</summary>
        public double Elongation { get; set; }

        /// <summary>周长（像素）。</summary>
        public double Perimeter { get; set; }
    }

    // ======================== 测量结果 ========================

    /// <summary>
    /// 测量类型枚举。
    /// </summary>
    public enum MeasurementType
    {
        /// <summary>两点距离。</summary>
        Distance,

        /// <summary>三点角度。</summary>
        Angle,

        /// <summary>圆拟合。</summary>
        CircleFit,

        /// <summary>直线拟合。</summary>
        LineFit
    }

    /// <summary>
    /// 测量结果。
    /// </summary>
    public class MeasurementResult
    {
        /// <summary>测量类型。</summary>
        public MeasurementType Type { get; set; }

        /// <summary>测量值（距离=mm，角度=度，圆=半径，线=斜率）。</summary>
        public double Value { get; set; }

        /// <summary>置信度（0~1）。</summary>
        public double Confidence { get; set; }

        /// <summary>描述信息。</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>附加参数（如圆心坐标、截距等）。</summary>
        public Dictionary<string, double> Parameters { get; set; } = new();
    }

    // ======================== 标定数据 ========================

    /// <summary>
    /// 标定数据 — 像素坐标到世界坐标的仿射变换参数。
    /// 通过 9 点标定法求解 2×3 仿射矩阵。
    /// </summary>
    public class CalibrationData
    {
        /// <summary>X 方向像素尺寸（mm/pixel）。</summary>
        public double PixelSizeX { get; set; }

        /// <summary>Y 方向像素尺寸（mm/pixel）。</summary>
        public double PixelSizeY { get; set; }

        /// <summary>
        /// 仿射变换矩阵（2×3）。
        /// [wx]   [M[0,0] M[0,1] M[0,2]]   [px]
        /// [wy] = [M[1,0] M[1,1] M[1,2]] × [py]
        ///                                   [ 1]
        /// </summary>
        public double[,] TransformMatrix { get; set; } = new double[2, 3];

        /// <summary>标定最大误差（像素）。</summary>
        public double MaxError { get; set; }

        /// <summary>标定平均误差（像素）。</summary>
        public double MeanError { get; set; }

        /// <summary>标定点数量。</summary>
        public int PointCount { get; set; }
    }

    // ======================== 相机配置 ========================

    /// <summary>
    /// 相机配置参数。
    /// </summary>
    public class CameraConfig
    {
        /// <summary>相机唯一标识。</summary>
        public string CameraId { get; set; } = string.Empty;

        /// <summary>驱动类型（如 "Simulated", "Hikvision", "Basler"）。</summary>
        public string DriverType { get; set; } = "Simulated";

        /// <summary>图像宽度（像素）。</summary>
        public int Width { get; set; } = 640;

        /// <summary>图像高度（像素）。</summary>
        public int Height { get; set; } = 480;

        /// <summary>曝光时间（毫秒）。</summary>
        public double ExposureMs { get; set; } = 10.0;

        /// <summary>增益。</summary>
        public double Gain { get; set; } = 1.0;
    }

    // ======================== 直方图数据 ========================

    /// <summary>
    /// 直方图数据 — 灰度图像的像素分布统计。
    /// </summary>
    public class HistogramData
    {
        /// <summary>各灰度级的像素计数（256 个 bin）。</summary>
        public int[] Bins { get; set; } = new int[256];

        /// <summary>灰度均值。</summary>
        public double Mean { get; set; }

        /// <summary>灰度标准差。</summary>
        public double StdDev { get; set; }

        /// <summary>OTSU 最佳阈值。</summary>
        public int OtsuThreshold { get; set; }

        /// <summary>总像素数。</summary>
        public int TotalPixels { get; set; }
    }

    // ======================== 管线步骤结果 ========================

    /// <summary>
    /// 管线单步执行结果 — 用于诊断和调试。
    /// </summary>
    public class PipelineStepResult
    {
        /// <summary>步骤名称。</summary>
        public string StepName { get; set; } = string.Empty;

        /// <summary>该步骤的输出图像。</summary>
        public ImageData Output { get; set; } = new();

        /// <summary>该步骤的执行耗时。</summary>
        public TimeSpan Duration { get; set; }
    }
}
