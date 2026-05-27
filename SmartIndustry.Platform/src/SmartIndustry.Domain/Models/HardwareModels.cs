// ============================================================
// 文件：HardwareModels.cs
// 层级：领域层 - 数据模型
// 职责：定义硬件抽象层所需的所有数据结构（运动、视觉、IO）。
//       这些模型是纯数据类（无业务逻辑），可被多层引用。
//       所有数值单位：位置=mm，速度=mm/s，加速度=mm/s²，角度=度。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Domain.Models
{
    // ============================================================
    // 一、运动控制相关数据模型
    // ============================================================

    /// <summary>
    /// 轴状态快照 — 描述单个运动轴在某一时刻的完整状态。
    /// 由运动控制卡驱动层填充，AxisController 据此驱动状态机。
    /// </summary>
    public class AxisStatus
    {
        /// <summary>轴索引（0-based）</summary>
        public int AxisIndex { get; set; }

        /// <summary>实际位置（编码器反馈，mm）</summary>
        public double ActualPosition { get; set; }

        /// <summary>指令位置（规划器输出，mm）</summary>
        public double CommandPosition { get; set; }

        /// <summary>实际速度（mm/s）</summary>
        public double ActualVelocity { get; set; }

        /// <summary>是否已使能（电机上电）</summary>
        public bool IsEnabled { get; set; }

        /// <summary>是否正在运动中</summary>
        public bool IsMoving { get; set; }

        /// <summary>是否已回零（建立了机械坐标系）</summary>
        public bool IsHomed { get; set; }

        /// <summary>正限位触发（true=碰到正限位开关）</summary>
        public bool PositiveLimitActive { get; set; }

        /// <summary>负限位触发（true=碰到负限位开关）</summary>
        public bool NegativeLimitActive { get; set; }

        /// <summary>原点信号有效（回零传感器触发）</summary>
        public bool HomeSensorActive { get; set; }

        /// <summary>是否有错误（报警）</summary>
        public bool HasError { get; set; }

        /// <summary>错误码（0=无错误）</summary>
        public int ErrorCode { get; set; }

        /// <summary>状态快照时间戳</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 回零配置参数 — 描述单轴的回零策略和速度参数。
    /// </summary>
    public class HomingConfig
    {
        /// <summary>快速搜索速度（mm/s，粗定位阶段）</summary>
        public double SearchVelocity { get; set; } = 50.0;

        /// <summary>慢速爬行速度（mm/s，精定位阶段）</summary>
        public double CreepVelocity { get; set; } = 5.0;

        /// <summary>回零方向（true=正向，false=负向）</summary>
        public bool Direction { get; set; } = false;

        /// <summary>回零后的偏移量（mm，回零完成后再移动此距离到零点）</summary>
        public double Offset { get; set; } = 0.0;

        /// <summary>回零超时时间（ms）</summary>
        public int TimeoutMs { get; set; } = 30000;
    }

    /// <summary>
    /// 运动速度参数 — 封装单次运动指令的运动学参数。
    /// </summary>
    public class MotionParameters
    {
        /// <summary>最大速度（mm/s）</summary>
        public double MaxVelocity { get; set; } = 100.0;

        /// <summary>加速度（mm/s²）</summary>
        public double Acceleration { get; set; } = 500.0;

        /// <summary>减速度（mm/s²，通常与加速度相同）</summary>
        public double Deceleration { get; set; } = 500.0;

        /// <summary>急动度 Jerk（mm/s³，仅 S 曲线规划使用）</summary>
        public double Jerk { get; set; } = 5000.0;
    }

    /// <summary>
    /// 软限位配置 — 定义轴的软件行程限制。
    /// 软限位在运动控制器层面检查，优先于硬件限位触发。
    /// </summary>
    public class SoftLimitConfig
    {
        /// <summary>是否启用软限位</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>正向软限位（mm）</summary>
        public double PositiveLimit { get; set; } = double.MaxValue;

        /// <summary>负向软限位（mm）</summary>
        public double NegativeLimit { get; set; } = double.MinValue;
    }

    // ============================================================
    // 二、视觉系统相关数据模型
    // ============================================================

    /// <summary>
    /// 图像数据 — 纯字节数组表示，不依赖任何 UI 框架。
    /// 像素按行优先存储：Pixels[y * Width * Channels + x * Channels + c]
    /// Channels=1 灰度图，Channels=3 BGR，Channels=4 BGRA
    /// </summary>
    public class ImageData
    {
        /// <summary>像素原始数据（字节数组）</summary>
        public byte[] Pixels { get; set; } = Array.Empty<byte>();

        /// <summary>图像宽度（像素）</summary>
        public int Width { get; set; }

        /// <summary>图像高度（像素）</summary>
        public int Height { get; set; }

        /// <summary>通道数（1=灰度，3=BGR，4=BGRA）</summary>
        public int Channels { get; set; } = 1;

        /// <summary>每行字节数（= Width × Channels）</summary>
        public int Stride => Width * Channels;

        /// <summary>采集时间戳</summary>
        public DateTime CaptureTime { get; set; } = DateTime.Now;

        /// <summary>相机/来源标识</summary>
        public string SourceId { get; set; } = string.Empty;

        /// <summary>
        /// 创建指定尺寸的空白图像
        /// </summary>
        public static ImageData Create(int width, int height, int channels = 1)
            => new() { Width = width, Height = height, Channels = channels, Pixels = new byte[width * height * channels] };

        /// <summary>深拷贝当前图像</summary>
        public ImageData Clone()
            => new() { Width = Width, Height = Height, Channels = Channels, Pixels = (byte[])Pixels.Clone(), CaptureTime = CaptureTime, SourceId = SourceId };

        /// <summary>获取指定位置灰度像素值</summary>
        public byte GetPixel(int x, int y) => Pixels[y * Stride + x * Channels];

        /// <summary>设置指定位置像素值（所有通道相同）</summary>
        public void SetPixel(int x, int y, byte value)
        {
            int offset = y * Stride + x * Channels;
            for (int c = 0; c < Channels; c++) Pixels[offset + c] = value;
        }
    }

    /// <summary>
    /// 通用视觉结果包装器 — 统一成功/失败/耗时信息。
    /// </summary>
    /// <typeparam name="T">算法结果数据类型</typeparam>
    public class VisionResult<T>
    {
        /// <summary>是否成功（算法正常完成）</summary>
        public bool IsSuccess { get; set; }

        /// <summary>结果数据（失败时可能为 null 或默认值）</summary>
        public T? Data { get; set; }

        /// <summary>错误信息（成功时为空）</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>算法执行耗时</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>结果时间戳</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>创建成功结果</summary>
        public static VisionResult<T> Success(T data, TimeSpan duration)
            => new() { IsSuccess = true, Data = data, Duration = duration };

        /// <summary>创建失败结果</summary>
        public static VisionResult<T> Failure(string error, TimeSpan duration)
            => new() { IsSuccess = false, ErrorMessage = error, Duration = duration };
    }

    /// <summary>
    /// 模板匹配结果 — 在图像中找到的匹配位置信息。
    /// </summary>
    public class TemplateMatchResult
    {
        /// <summary>匹配得分（0~1，1=完美匹配）</summary>
        public double Score { get; set; }

        /// <summary>匹配中心X坐标（像素）</summary>
        public double X { get; set; }

        /// <summary>匹配中心Y坐标（像素）</summary>
        public double Y { get; set; }

        /// <summary>匹配角度（度，旋转不变匹配）</summary>
        public double Angle { get; set; }

        /// <summary>是否找到有效匹配（得分超过阈值）</summary>
        public bool Found { get; set; }
    }

    /// <summary>
    /// Blob 分析参数
    /// </summary>
    public class BlobAnalysisParameters
    {
        /// <summary>二值化阈值（0=自动Otsu，1~255=固定阈值）</summary>
        public int Threshold { get; set; } = 0;

        /// <summary>最小 Blob 面积（像素，过滤噪声）</summary>
        public int MinArea { get; set; } = 100;

        /// <summary>最大 Blob 面积（像素，过滤过大区域）</summary>
        public int MaxArea { get; set; } = int.MaxValue;
    }

    /// <summary>
    /// Blob 信息 — 单个连通域的特征描述。
    /// </summary>
    public class BlobInfo
    {
        /// <summary>Blob 编号（从1开始）</summary>
        public int Id { get; set; }

        /// <summary>面积（像素数）</summary>
        public int Area { get; set; }

        /// <summary>质心X坐标（像素）</summary>
        public double CenterX { get; set; }

        /// <summary>质心Y坐标（像素）</summary>
        public double CenterY { get; set; }

        /// <summary>外接矩形 X</summary>
        public int BoundX { get; set; }

        /// <summary>外接矩形 Y</summary>
        public int BoundY { get; set; }

        /// <summary>外接矩形宽度</summary>
        public int BoundWidth { get; set; }

        /// <summary>外接矩形高度</summary>
        public int BoundHeight { get; set; }

        /// <summary>圆度（4π×面积/周长²，1.0=完美圆）</summary>
        public double Circularity { get; set; }

        /// <summary>周长（像素）</summary>
        public double Perimeter { get; set; }
    }

    /// <summary>
    /// 尺寸测量参数
    /// </summary>
    public class MeasurementParameters
    {
        /// <summary>测量类型标识（如"Width"、"Diameter"）</summary>
        public string MeasureType { get; set; } = string.Empty;

        /// <summary>基准尺寸（mm，用于偏差计算）</summary>
        public double NominalValue { get; set; }

        /// <summary>上公差（mm）</summary>
        public double UpperTolerance { get; set; } = 0.1;

        /// <summary>下公差（mm）</summary>
        public double LowerTolerance { get; set; } = -0.1;

        /// <summary>像素精度比（mm/pixel）</summary>
        public double PixelSize { get; set; } = 0.01;
    }

    /// <summary>
    /// 尺寸测量结果
    /// </summary>
    public class MeasurementResult
    {
        /// <summary>测量值（mm）</summary>
        public double MeasuredValue { get; set; }

        /// <summary>与标称值的偏差（mm）</summary>
        public double Deviation { get; set; }

        /// <summary>是否在公差范围内</summary>
        public bool IsInTolerance { get; set; }

        /// <summary>测量置信度（0~1）</summary>
        public double Confidence { get; set; }

        /// <summary>描述信息</summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// OCR 识别结果
    /// </summary>
    public class OcrResult
    {
        /// <summary>识别出的文本内容</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>识别置信度（0~1）</summary>
        public double Confidence { get; set; }

        /// <summary>识别类型（Text/Barcode/QRCode）</summary>
        public string RecognitionType { get; set; } = "Text";
    }

    /// <summary>
    /// 缺陷检测结果
    /// </summary>
    public class DefectResult
    {
        /// <summary>判定结果</summary>
        public DefectJudgment Judgment { get; set; }

        /// <summary>检测出的缺陷列表（OK时为空列表）</summary>
        public List<DefectItem> Defects { get; set; } = new();

        /// <summary>总体置信度</summary>
        public double Confidence { get; set; }
    }

    /// <summary>
    /// 缺陷判定枚举
    /// </summary>
    public enum DefectJudgment
    {
        /// <summary>合格</summary>
        OK = 0,

        /// <summary>不合格</summary>
        NG = 1,

        /// <summary>不确定（需人工复判）</summary>
        Uncertain = 2
    }

    /// <summary>
    /// 单个缺陷信息
    /// </summary>
    public class DefectItem
    {
        /// <summary>缺陷类型（划痕、气泡、污点等）</summary>
        public string DefectType { get; set; } = string.Empty;

        /// <summary>缺陷位置X（像素）</summary>
        public double X { get; set; }

        /// <summary>缺陷位置Y（像素）</summary>
        public double Y { get; set; }

        /// <summary>缺陷面积（像素）</summary>
        public double Area { get; set; }

        /// <summary>严重程度（0~1）</summary>
        public double Severity { get; set; }
    }

    /// <summary>
    /// 标定数据 — 像素坐标到机械坐标的仿射变换参数。
    /// 通过9点标定法（最小二乘）求解 2x3 仿射矩阵。
    /// </summary>
    public class CalibrationData
    {
        /// <summary>
        /// 仿射变换矩阵（2行3列）：
        /// [wx]   [M[0,0]  M[0,1]  M[0,2]]   [px]
        /// [wy] = [M[1,0]  M[1,1]  M[1,2]] * [py]
        ///                                     [ 1]
        /// </summary>
        public double[,] AffineMatrix { get; set; } = new double[2, 3];

        /// <summary>X方向像素尺寸（mm/pixel）</summary>
        public double PixelSizeX { get; set; }

        /// <summary>Y方向像素尺寸（mm/pixel）</summary>
        public double PixelSizeY { get; set; }

        /// <summary>标定 RMS 误差（mm）</summary>
        public double RmsError { get; set; }

        /// <summary>最大单点误差（mm）</summary>
        public double MaxError { get; set; }

        /// <summary>标定点数量</summary>
        public int PointCount { get; set; }

        /// <summary>标定时间</summary>
        public DateTime CalibratedAt { get; set; }

        /// <summary>是否有效（已完成标定）</summary>
        public bool IsValid { get; set; }
    }

    // ============================================================
    // 三、IO相关数据模型
    // ============================================================

    /// <summary>
    /// IO通道类型枚举
    /// </summary>
    public enum IoChannelType
    {
        /// <summary>数字输入</summary>
        DigitalInput = 0,

        /// <summary>数字输出</summary>
        DigitalOutput = 1,

        /// <summary>模拟输入</summary>
        AnalogInput = 2,

        /// <summary>模拟输出</summary>
        AnalogOutput = 3
    }

    /// <summary>
    /// IO通道定义 — 描述单个IO点的配置和当前状态。
    /// </summary>
    public class IoChannel
    {
        /// <summary>通道地址（设备内唯一）</summary>
        public int Address { get; set; }

        /// <summary>通道名称（如"夹爪1_IN"）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>通道类型</summary>
        public IoChannelType Type { get; set; }

        /// <summary>当前数字值（仅 DI/DO 有效）</summary>
        public bool DigitalValue { get; set; }

        /// <summary>当前模拟值（仅 AI/AO 有效）</summary>
        public double AnalogValue { get; set; }

        /// <summary>最后更新时间</summary>
        public DateTime LastUpdate { get; set; } = DateTime.Now;

        /// <summary>是否反相（信号取反，适配不同硬件接线方式）</summary>
        public bool IsInverted { get; set; }
    }

    /// <summary>
    /// IO变化事件数据 — 当某个IO通道状态发生变化时通过 IEventBus 发布。
    /// </summary>
    public class IoChangedEvent
    {
        /// <summary>设备标识</summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>变化的通道快照</summary>
        public IoChannel Channel { get; set; } = new();

        /// <summary>变化类型（RisingEdge/FallingEdge/AnalogChange）</summary>
        public string ChangeType { get; set; } = string.Empty;
    }

    // ============================================================
    // 四、运动事件数据模型
    // ============================================================

    /// <summary>
    /// 运动完成事件 — 轴到达目标位置时通过 IEventBus 发布。
    /// </summary>
    public class MotionCompletedEvent
    {
        /// <summary>轴标识（如"X"、"Y1"）</summary>
        public string AxisId { get; set; } = string.Empty;

        /// <summary>目标位置（mm）</summary>
        public double TargetPosition { get; set; }

        /// <summary>实际到达位置（mm）</summary>
        public double ActualPosition { get; set; }

        /// <summary>定位误差（mm）</summary>
        public double PositionError { get; set; }

        /// <summary>运动耗时</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>是否成功（false=超时/限位等异常停止）</summary>
        public bool IsSuccess { get; set; }
    }

    /// <summary>
    /// 轴错误事件 — 轴发生错误时通过 IEventBus 发布。
    /// </summary>
    public class AxisErrorEvent
    {
        /// <summary>轴标识</summary>
        public string AxisId { get; set; } = string.Empty;

        /// <summary>错误码</summary>
        public int ErrorCode { get; set; }

        /// <summary>错误描述</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>发生时间</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    // ============================================================
    // 五、直方图数据模型
    // ============================================================

    /// <summary>
    /// 灰度直方图数据
    /// </summary>
    public class HistogramData
    {
        /// <summary>各灰度级的像素计数（256个bin，下标=灰度值0~255）</summary>
        public int[] Bins { get; set; } = new int[256];

        /// <summary>灰度均值</summary>
        public double Mean { get; set; }

        /// <summary>灰度标准差</summary>
        public double StdDev { get; set; }

        /// <summary>Otsu最佳阈值</summary>
        public int OtsuThreshold { get; set; }

        /// <summary>总像素数</summary>
        public int TotalPixels { get; set; }
    }
}
