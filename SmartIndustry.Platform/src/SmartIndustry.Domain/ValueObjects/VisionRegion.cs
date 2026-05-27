// ============================================================
// 文件：VisionRegion.cs
// 层次：领域层 (Domain Layer) — 值对象
// 职责：描述视觉检测的感兴趣区域（ROI, Region of Interest）
// 设计思路：
//   视觉 ROI 是视觉算法中的基础概念，描述在图像中需要处理的矩形区域。
//   支持旋转角度，用于处理倾斜的零件或需要角度对齐的场景。
//   使用 readonly record struct 保证值语义和不可变性。
//   提供常用的几何计算方法（面积、中心点、扩展/收缩），
//   便于视觉算法参数化配置。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Domain.ValueObjects
{
    /// <summary>
    /// 视觉感兴趣区域值对象（ROI）。
    /// 表示图像中的一个可旋转矩形区域，用于约束视觉算法的处理范围。
    /// 坐标系：以图像左上角为原点，X 轴向右，Y 轴向下（图像坐标系）。
    /// </summary>
    public readonly record struct VisionRegion
    {
        // ----------------------------------------------------------------
        // 区域属性
        // ----------------------------------------------------------------

        /// <summary>
        /// 区域左上角的 X 坐标（像素）。
        /// 当 Angle=0 时，为矩形左上角的水平像素位置。
        /// </summary>
        public double X { get; init; }

        /// <summary>
        /// 区域左上角的 Y 坐标（像素）。
        /// 当 Angle=0 时，为矩形左上角的垂直像素位置。
        /// </summary>
        public double Y { get; init; }

        /// <summary>
        /// 区域宽度（像素）。必须大于零。
        /// </summary>
        public double Width { get; init; }

        /// <summary>
        /// 区域高度（像素）。必须大于零。
        /// </summary>
        public double Height { get; init; }

        /// <summary>
        /// 矩形旋转角度（度，顺时针为正）。
        /// 范围：-180.0 ~ 180.0 度。
        /// 0 度表示水平对齐（不旋转），90 度表示矩形旋转 90°（竖直方向）。
        /// </summary>
        public double Angle { get; init; }

        // ----------------------------------------------------------------
        // 构造函数
        // ----------------------------------------------------------------

        /// <summary>
        /// 创建视觉感兴趣区域。
        /// </summary>
        /// <param name="x">左上角 X 坐标（像素）</param>
        /// <param name="y">左上角 Y 坐标（像素）</param>
        /// <param name="width">宽度（像素，必须大于零）</param>
        /// <param name="height">高度（像素，必须大于零）</param>
        /// <param name="angle">旋转角度（度，默认 0，不旋转）</param>
        /// <exception cref="ArgumentOutOfRangeException">宽度或高度非正时抛出</exception>
        public VisionRegion(double x, double y, double width, double height, double angle = 0.0)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "视觉区域宽度必须大于零。");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "视觉区域高度必须大于零。");

            X = x;
            Y = y;
            Width = width;
            Height = height;
            // 将角度规范化到 [-180, 180] 范围
            Angle = NormalizeAngle(angle);
        }

        // ----------------------------------------------------------------
        // 常用几何计算属性
        // ----------------------------------------------------------------

        /// <summary>区域面积（像素²）= 宽度 × 高度</summary>
        public double Area => Width * Height;

        /// <summary>区域中心点的 X 坐标（旋转前的中心，未考虑旋转偏移）</summary>
        public double CenterX => X + Width / 2.0;

        /// <summary>区域中心点的 Y 坐标（旋转前的中心，未考虑旋转偏移）</summary>
        public double CenterY => Y + Height / 2.0;

        /// <summary>区域的宽高比（Width / Height），用于形状验证</summary>
        public double AspectRatio => Width / Height;

        /// <summary>区域右边缘的 X 坐标（X + Width）</summary>
        public double Right => X + Width;

        /// <summary>区域底边缘的 Y 坐标（Y + Height）</summary>
        public double Bottom => Y + Height;

        // ----------------------------------------------------------------
        // 几何变换方法
        // ----------------------------------------------------------------

        /// <summary>
        /// 按指定像素数向四周扩展区域（返回新的 VisionRegion）。
        /// 典型用法：在精确 ROI 外额外增加搜索余量，应对轻微位移。
        /// </summary>
        /// <param name="pixels">扩展像素数（负数则收缩）</param>
        public VisionRegion Expand(double pixels) =>
            new(X - pixels, Y - pixels, Width + pixels * 2, Height + pixels * 2, Angle);

        /// <summary>
        /// 按指定像素数向内收缩区域（返回新的 VisionRegion）。
        /// 典型用法：去除边缘噪声后的有效检测区域。
        /// </summary>
        /// <param name="pixels">收缩像素数（正值）</param>
        public VisionRegion Shrink(double pixels) => Expand(-pixels);

        /// <summary>
        /// 平移区域到新位置（返回新的 VisionRegion）。
        /// </summary>
        /// <param name="dx">X 方向偏移量（像素）</param>
        /// <param name="dy">Y 方向偏移量（像素）</param>
        public VisionRegion Translate(double dx, double dy) =>
            new(X + dx, Y + dy, Width, Height, Angle);

        /// <summary>
        /// 按比例缩放区域（相对于左上角缩放，返回新的 VisionRegion）。
        /// 典型用法：相机分辨率变化时自适应调整 ROI。
        /// </summary>
        /// <param name="scaleX">X 方向缩放比例</param>
        /// <param name="scaleY">Y 方向缩放比例</param>
        public VisionRegion Scale(double scaleX, double scaleY) =>
            new(X * scaleX, Y * scaleY, Width * scaleX, Height * scaleY, Angle);

        /// <summary>
        /// 旋转区域角度（在当前角度基础上叠加，返回新的 VisionRegion）。
        /// </summary>
        /// <param name="degrees">旋转角度增量（度）</param>
        public VisionRegion Rotate(double degrees) =>
            new(X, Y, Width, Height, Angle + degrees);

        /// <summary>
        /// 判断指定像素点是否在区域内（仅对 Angle=0 的矩形有效）。
        /// </summary>
        /// <param name="px">像素 X 坐标</param>
        /// <param name="py">像素 Y 坐标</param>
        public bool Contains(double px, double py) =>
            px >= X && px <= Right && py >= Y && py <= Bottom;

        /// <summary>
        /// 判断两个区域是否有交叉（仅对 Angle=0 的矩形有效，忽略旋转）。
        /// </summary>
        public bool Intersects(VisionRegion other) =>
            !(Right < other.X || X > other.Right ||
              Bottom < other.Y || Y > other.Bottom);

        // ----------------------------------------------------------------
        // 私有辅助方法
        // ----------------------------------------------------------------

        /// <summary>
        /// 将角度规范化到 (-180, 180] 范围。
        /// </summary>
        private static double NormalizeAngle(double angle)
        {
            while (angle > 180.0) angle -= 360.0;
            while (angle <= -180.0) angle += 360.0;
            return angle;
        }

        // ----------------------------------------------------------------
        // 字符串表示
        // ----------------------------------------------------------------

        /// <summary>
        /// 返回调试友好的区域描述字符串。
        /// 示例：[ROI: X=100.0, Y=200.0, W=320.0, H=240.0, Angle=0.0°]
        /// </summary>
        public override string ToString()
            => $"[ROI: X={X:F1}, Y={Y:F1}, W={Width:F1}, H={Height:F1}, Angle={Angle:F1}°]";
    }
}
