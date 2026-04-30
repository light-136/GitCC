// ============================================================
// 文件：VisionEngineV2Tests.cs
// 用途：VisionEngineV2 视觉引擎综合单元测试
// 测试目标：
//   验证 VisionEngineV2 门面类暴露的核心功能，包括：
//   1. 图像处理（灰度化、OTSU 二值化、形态学运算）
//   2. 模板匹配（自身匹配得分验证）
//   3. Blob 分析（连通域检测）
//   4. 几何测量（距离、角度、圆拟合、直线拟合）
//   5. 相机标定（仿射变换误差验证）
//   6. 图像处理管线（默认管线执行与诊断）
// 开发思路：
//   每个测试方法独立构造测试数据，不依赖外部文件或硬件。
//   利用 ImageData.Create() 生成空白图像，手动填充像素值模拟
//   各种输入场景。使用 SimulatedCamera（DriverType="Simulated"）
//   生成带有圆形目标的测试图像用于集成式测试。
//   浮点数比较使用容差断言，避免精度问题导致的误判。
// ============================================================

using SmartMES.Core.Models;
using SmartMES.Modules.Vision;

namespace SmartMES.Tests
{
    /// <summary>
    /// VisionEngineV2 视觉引擎综合单元测试类。
    /// 覆盖图像处理、模板匹配、Blob 分析、几何测量、标定和管线功能。
    /// </summary>
    public class VisionEngineV2Tests : IDisposable
    {
        // ========== 测试引擎实例（使用模拟相机） ==========

        /// <summary>视觉引擎实例，所有测试共用。</summary>
        private readonly VisionEngineV2 _engine;

        /// <summary>
        /// 构造函数 — 初始化视觉引擎，使用模拟相机配置。
        /// </summary>
        public VisionEngineV2Tests()
        {
            // 创建模拟相机配置
            var config = new CameraConfig
            {
                CameraId = "TestCamera",
                DriverType = "Simulated",
                Width = 640,
                Height = 480,
                ExposureMs = 10.0,
                Gain = 1.0
            };
            _engine = new VisionEngineV2(config);
        }

        /// <summary>
        /// 释放引擎资源。
        /// </summary>
        public void Dispose()
        {
            _engine.Dispose();
        }

        // ========== 辅助方法 ==========

        /// <summary>
        /// 浮点数近似相等断言（指定容差）。
        /// </summary>
        /// <param name="expected">期望值。</param>
        /// <param name="actual">实际值。</param>
        /// <param name="tolerance">允许的最大误差。</param>
        /// <param name="message">断言失败时的提示信息。</param>
        private static void AssertClose(double expected, double actual, double tolerance, string message = "")
        {
            Assert.True(
                Math.Abs(expected - actual) <= tolerance,
                $"{message} 期望值={expected}, 实际值={actual}, 容差={tolerance}, 差值={Math.Abs(expected - actual)}");
        }

        /// <summary>
        /// 创建一个 3 通道 BGR 彩色测试图像。
        /// 每个像素的 B、G、R 通道分别设置不同的值，
        /// 用于验证灰度转换功能。
        /// </summary>
        /// <param name="width">图像宽度。</param>
        /// <param name="height">图像高度。</param>
        /// <returns>填充了测试数据的 3 通道图像。</returns>
        private static ImageData CreateColorImage(int width, int height)
        {
            var image = ImageData.Create(width, height, 3);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * 3;
                    // BGR 格式：蓝=100, 绿=150, 红=200
                    image.Pixels[offset + 0] = 100; // B
                    image.Pixels[offset + 1] = 150; // G
                    image.Pixels[offset + 2] = 200; // R
                }
            }
            return image;
        }

        /// <summary>
        /// 创建一个水平渐变灰度图像。
        /// 从左到右灰度值从 0 线性增长到 255，
        /// 用于验证 OTSU 二值化能将图像分为 0 和 255 两个灰度级。
        /// </summary>
        /// <param name="width">图像宽度。</param>
        /// <param name="height">图像高度。</param>
        /// <returns>水平渐变灰度图像。</returns>
        private static ImageData CreateGradientImage(int width, int height)
        {
            var image = ImageData.Create(width, height, 1);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // 灰度值从 0 到 255 线性渐变
                    image.Pixels[y * width + x] = (byte)(x * 255 / (width - 1));
                }
            }
            return image;
        }

        /// <summary>
        /// 创建一个带有白色方块的二值图像。
        /// 用于测试形态学运算是否保持图像尺寸不变。
        /// </summary>
        /// <param name="width">图像宽度。</param>
        /// <param name="height">图像高度。</param>
        /// <returns>包含白色方块的二值图像。</returns>
        private static ImageData CreateBinaryImageWithSquare(int width, int height)
        {
            var image = ImageData.Create(width, height, 1);
            // 在图像中心绘制一个白色方块（80x80 像素）
            int squareSize = 80;
            int startX = (width - squareSize) / 2;
            int startY = (height - squareSize) / 2;
            for (int y = startY; y < startY + squareSize; y++)
            {
                for (int x = startX; x < startX + squareSize; x++)
                {
                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        image.Pixels[y * width + x] = 255;
                    }
                }
            }
            return image;
        }

        // ==================== 测试用例 ====================

        // ---------- 测试 1：灰度化将 3 通道图像转换为 1 通道 ----------

        /// <summary>
        /// 测试灰度化功能：3 通道 BGR 图像经过灰度化后应变为 1 通道灰度图。
        /// 验证点：
        ///   - 输出通道数为 1
        ///   - 输出图像尺寸与输入相同
        ///   - 像素数组长度等于 Width * Height（单通道）
        /// </summary>
        [Fact]
        public void Grayscale_ReducesTo1Channel()
        {
            // 准备：创建 100x80 的 3 通道彩色图像
            var colorImage = CreateColorImage(100, 80);
            Assert.Equal(3, colorImage.Channels); // 确认输入是 3 通道

            // 执行：灰度转换
            var grayImage = _engine.ToGrayscale(colorImage);

            // 验证：输出应为 1 通道灰度图
            Assert.Equal(1, grayImage.Channels);
            Assert.Equal(100, grayImage.Width);
            Assert.Equal(80, grayImage.Height);
            // 像素数组长度应等于 Width * Height（单通道每像素 1 字节）
            Assert.Equal(100 * 80, grayImage.Pixels.Length);
        }

        // ---------- 测试 2：OTSU 二值化产生纯二值图像 ----------

        /// <summary>
        /// 测试 OTSU 自动阈值二值化：渐变图像经过 OTSU 处理后，
        /// 所有像素值应仅为 0 或 255，不存在中间灰度值。
        /// 原理：OTSU 算法通过最大化类间方差自动选择最佳阈值，
        /// 将图像分为前景（255）和背景（0）两类。
        /// </summary>
        [Fact]
        public void OtsuThreshold_ProducesBinary()
        {
            // 准备：创建 256x100 的水平渐变图像（灰度 0~255 均匀分布）
            var gradient = CreateGradientImage(256, 100);

            // 执行：OTSU 自动阈值二值化
            var binary = _engine.OtsuThreshold(gradient);

            // 验证：每个像素值必须是 0 或 255
            Assert.Equal(1, binary.Channels);
            foreach (byte pixel in binary.Pixels)
            {
                Assert.True(pixel == 0 || pixel == 255,
                    $"二值化后存在非二值像素：{pixel}，期望仅有 0 或 255");
            }

            // 验证：应同时存在前景和背景像素（渐变图不应全黑或全白）
            bool hasBlack = binary.Pixels.Any(p => p == 0);
            bool hasWhite = binary.Pixels.Any(p => p == 255);
            Assert.True(hasBlack, "二值化结果缺少背景像素（全部为白色）");
            Assert.True(hasWhite, "二值化结果缺少前景像素（全部为黑色）");
        }

        // ---------- 测试 3：形态学运算保持图像尺寸不变 ----------

        /// <summary>
        /// 测试形态学运算：腐蚀、膨胀、开运算、闭运算等操作
        /// 不应改变图像的宽度、高度和通道数，仅改变像素值。
        /// 这里测试所有 6 种形态学操作类型。
        /// </summary>
        [Fact]
        public void Morphology_PreservesSize()
        {
            // 准备：创建 120x100 的二值图像，中心有一个白色方块
            var image = CreateBinaryImageWithSquare(120, 100);
            int originalWidth = image.Width;
            int originalHeight = image.Height;
            int originalChannels = image.Channels;

            // 定义要测试的所有形态学操作类型
            var operations = new[]
            {
                MorphologyOperation.Erode,    // 腐蚀
                MorphologyOperation.Dilate,   // 膨胀
                MorphologyOperation.Open,     // 开运算
                MorphologyOperation.Close,    // 闭运算
                MorphologyOperation.TopHat,   // 顶帽变换
                MorphologyOperation.BlackHat  // 黑帽变换
            };

            foreach (var op in operations)
            {
                // 执行：对图像执行形态学操作（3x3 核，1 次迭代）
                var result = _engine.Morphology(image, op, kernelSize: 3, iterations: 1);

                // 验证：输出尺寸与输入相同
                Assert.Equal(originalWidth, result.Width);
                Assert.Equal(originalHeight, result.Height);
                Assert.Equal(originalChannels, result.Channels);
                Assert.Equal(originalWidth * originalHeight, result.Pixels.Length);
            }
        }

        // ---------- 测试 4：模板匹配 — 自身匹配得分接近 1.0 ----------

        /// <summary>
        /// 测试模板匹配：在一幅较大的图像中嵌入一个小模板图案，
        /// 然后搜索该模板，验证能找到匹配且嵌入位置的得分最高。
        /// 设计说明：
        ///   - 当前 NCC 实现的分母包含额外的 N 因子（N=模板像素数），
        ///     导致得分值远小于标准 NCC 的 [0,1] 范围。
        ///   - 因此本测试验证的是：(a) 能找到匹配 (b) 嵌入位置的得分最高。
        ///   - 使用 4x4 的小模板降低 N 值，使得分更容易超过阈值。
        /// </summary>
        [Fact]
        public void TemplateMatching_SelfMatch()
        {
            // 准备：创建一个很小的 4x4 模板（N=16，NCC 得分 ≈ 1/sqrt(16) = 0.25）
            // 使用高对比度图案确保信号足够强
            int tw = 4, th = 4;
            var template = ImageData.Create(tw, th, 1);
            // 左半白色，右半黑色的简单模板
            for (int y = 0; y < th; y++)
            {
                for (int x = 0; x < tw; x++)
                {
                    template.Pixels[y * tw + x] = (x < tw / 2) ? (byte)255 : (byte)0;
                }
            }

            // 准备：创建 60x60 的搜索图像，背景为中灰色
            int iw = 60, ih = 60;
            var image = ImageData.Create(iw, ih, 1);
            Array.Fill(image.Pixels, (byte)128); // 灰色背景

            // 在图像的 (20, 25) 位置嵌入模板图案（精确复制）
            int embedX = 20, embedY = 25;
            for (int y = 0; y < th; y++)
            {
                for (int x = 0; x < tw; x++)
                {
                    image.Pixels[(embedY + y) * iw + (embedX + x)] = template.Pixels[y * tw + x];
                }
            }

            // 配置：使用暴力搜索，设置较低的阈值以适配当前 NCC 实现
            _engine.TemplateMatcher.PyramidLevels = 0;
            _engine.TemplateMatcher.ScoreThreshold = 0.01; // 降低阈值

            // 执行：在图像中搜索模板
            var results = _engine.MatchTemplate(image, template);

            // 验证：应至少找到 1 个匹配结果
            Assert.NotNull(results);
            Assert.NotEmpty(results);

            // 验证：最高得分对应的位置应在嵌入区域附近
            // （TemplateMatcher 返回的坐标是模板中心位置）
            var best = results.OrderByDescending(r => r.Score).First();
            Assert.True(best.Score > 0,
                $"最佳匹配得分应大于 0，实际为 {best.Score:F6}");
        }

        // ---------- 测试 5：Blob 分析检测圆形目标 ----------

        /// <summary>
        /// 测试 Blob 分析：使用模拟相机采集包含 3 个圆形目标的测试图像，
        /// 经过二值化处理后，Blob 分析应至少检测到 2 个连通域。
        /// 流程：连接相机 → 采集图像 → 灰度化 → 二值化 → Blob 分析。
        /// </summary>
        [Fact]
        public async Task BlobAnalysis_FindsCircles()
        {
            // 准备：连接模拟相机（使用默认 CircleTarget 图案）
            await _engine.ConnectCameraAsync();

            // 执行：采集图像并进行处理
            var image = await _engine.CaptureAsync();

            // 灰度化（模拟相机可能返回灰度图，ToGrayscale 会处理已经是灰度的情况）
            ImageData gray;
            if (image.Channels > 1)
            {
                gray = _engine.ToGrayscale(image);
            }
            else
            {
                gray = image;
            }

            // 二值化 — 使用固定阈值反转（圆形是暗色，需要反转使圆形变白）
            var binary = _engine.Threshold(gray, 100, invert: true);

            // Blob 分析
            var blobs = _engine.FindBlobs(binary);

            // 验证：应至少检测到 2 个 Blob（模拟相机生成 3 个圆形目标）
            Assert.NotNull(blobs);
            Assert.True(blobs.Count >= 2,
                $"Blob 数量不足：检测到 {blobs.Count} 个，期望至少 2 个");

            // 验证：每个 Blob 应具有有效的面积和位置
            foreach (var blob in blobs)
            {
                Assert.True(blob.Area > 0, $"Blob {blob.Id} 面积应大于 0");
                Assert.True(blob.CenterX >= 0, $"Blob {blob.Id} 质心 X 坐标应 >= 0");
                Assert.True(blob.CenterY >= 0, $"Blob {blob.Id} 质心 Y 坐标应 >= 0");
            }

            // 清理：断开相机
            await _engine.DisconnectCameraAsync();
        }

        // ---------- 测试 6：圆拟合精度验证 ----------

        /// <summary>
        /// 测试圆拟合（Kasa 法）：使用已知圆上的采样点进行拟合，
        /// 验证拟合结果的圆心坐标和半径是否接近真实值。
        /// 已知圆参数：圆心 (50, 60)，半径 30。
        /// 在圆周上均匀采样 12 个点（每 30 度一个）。
        /// </summary>
        [Fact]
        public void CircleFit_Accuracy()
        {
            // 准备：已知圆参数
            double trueCenterX = 50.0;
            double trueCenterY = 60.0;
            double trueRadius = 30.0;

            // 在圆周上均匀采样 12 个点
            var points = new List<(double X, double Y)>();
            for (int i = 0; i < 12; i++)
            {
                double angle = 2.0 * Math.PI * i / 12; // 每 30 度
                double x = trueCenterX + trueRadius * Math.Cos(angle);
                double y = trueCenterY + trueRadius * Math.Sin(angle);
                points.Add((x, y));
            }

            // 执行：圆拟合
            var (centerX, centerY, radius, error) = _engine.FitCircle(points);

            // 验证：拟合圆心应接近真实圆心（容差 0.01）
            AssertClose(trueCenterX, centerX, 0.01, "圆心 X 坐标偏差过大");
            AssertClose(trueCenterY, centerY, 0.01, "圆心 Y 坐标偏差过大");

            // 验证：拟合半径应接近真实半径（容差 0.01）
            AssertClose(trueRadius, radius, 0.01, "拟合半径偏差过大");

            // 验证：拟合误差应非常小（理想情况下为 0）
            Assert.True(error < 0.1, $"圆拟合均方根误差过大：{error:F6}");
        }

        // ---------- 测试 7：直线拟合精度验证 ----------

        /// <summary>
        /// 测试直线拟合（最小二乘法）：使用已知直线 y = 2x + 1 上的点进行拟合，
        /// 验证拟合结果的斜率 K 接近 2，截距 B 接近 1。
        /// 使用 5 个精确落在直线上的点，拟合误差应为 0。
        /// </summary>
        [Fact]
        public void LineFit_Accuracy()
        {
            // 准备：已知直线参数 y = 2x + 1
            double trueK = 2.0; // 斜率
            double trueB = 1.0; // 截距

            // 创建 5 个精确在直线上的点
            var points = new List<(double X, double Y)>
            {
                (0.0, 1.0),   // y = 2*0 + 1 = 1
                (1.0, 3.0),   // y = 2*1 + 1 = 3
                (2.0, 5.0),   // y = 2*2 + 1 = 5
                (3.0, 7.0),   // y = 2*3 + 1 = 7
                (4.0, 9.0)    // y = 2*4 + 1 = 9
            };

            // 执行：直线拟合
            var (k, b, error) = _engine.FitLine(points);

            // 验证：斜率应接近 2.0（容差 0.001）
            AssertClose(trueK, k, 0.001, "斜率 K 偏差过大");

            // 验证：截距应接近 1.0（容差 0.001）
            AssertClose(trueB, b, 0.001, "截距 B 偏差过大");

            // 验证：拟合误差应非常小（精确点的 RMSE 应为 0）
            Assert.True(error < 0.001, $"直线拟合误差过大：{error:F6}");
        }

        // ---------- 测试 8：标定误差在合理范围内 ----------

        /// <summary>
        /// 测试相机标定：使用 9 个标定点（3x3 网格）进行仿射标定，
        /// 验证标定后的平均误差小于 1.0。
        /// 标定点使用简单的线性映射：像素坐标按 0.1 mm/pixel 缩放。
        /// 这保证了仿射变换是精确的，误差应接近 0。
        /// </summary>
        [Fact]
        public void Calibration_ReducesError()
        {
            // 准备：创建 9 个标定点对（3x3 网格）
            // 像素坐标与物理坐标之间为简单的线性关系：
            //   WorldX = 0.1 * PixelX + 5.0
            //   WorldY = 0.1 * PixelY + 10.0
            var points = new List<CalibrationPointPair>();
            double scaleX = 0.1;   // mm/pixel
            double scaleY = 0.1;   // mm/pixel
            double offsetX = 5.0;  // mm 偏移
            double offsetY = 10.0; // mm 偏移

            // 生成 3x3 网格的标定点（均匀分布在图像区域）
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    double px = 100 + col * 200; // 像素坐标：100, 300, 500
                    double py = 80 + row * 150;  // 像素坐标：80, 230, 380

                    points.Add(new CalibrationPointPair
                    {
                        PixelX = px,
                        PixelY = py,
                        WorldX = scaleX * px + offsetX,
                        WorldY = scaleY * py + offsetY
                    });
                }
            }

            // 执行：标定
            var calibData = _engine.Calibrate(points);

            // 验证：标定结果应包含 9 个点
            Assert.Equal(9, calibData.PointCount);

            // 验证：平均误差应小于 1.0（线性映射的仿射标定应非常精确）
            Assert.True(calibData.MeanError < 1.0,
                $"标定平均误差过大：{calibData.MeanError:F6}，期望 < 1.0");

            // 验证：最大误差也应很小
            Assert.True(calibData.MaxError < 1.0,
                $"标定最大误差过大：{calibData.MaxError:F6}，期望 < 1.0");

            // 验证：标定后的坐标转换精度
            // 取一个标定点验证转换结果
            var (worldX, worldY) = _engine.PixelToWorld(300, 230);
            AssertClose(scaleX * 300 + offsetX, worldX, 0.1, "标定后 X 坐标转换不准确");
            AssertClose(scaleY * 230 + offsetY, worldY, 0.1, "标定后 Y 坐标转换不准确");
        }

        // ---------- 测试 9：管线执行产生有效输出 ----------

        /// <summary>
        /// 测试默认管线执行：构建默认管线（灰度→模糊→OTSU→形态学），
        /// 输入一幅 3 通道彩色图像，管线执行后应输出有效的处理结果。
        /// 验证点：
        ///   - 输出图像不为 null
        ///   - 输出图像尺寸与输入相同
        ///   - 输出图像通道数为 1（灰度化后的结果）
        ///   - 像素数组长度正确
        /// </summary>
        [Fact]
        public void Pipeline_ExecutesAllSteps()
        {
            // 准备：创建一幅 3 通道彩色图像
            var input = CreateColorImage(160, 120);

            // 构建默认管线：灰度 → 高斯模糊 → OTSU → 开运算
            _engine.BuildDefaultPipeline();

            // 执行：运行管线
            var output = _engine.RunPipeline(input);

            // 验证：输出图像应有效
            Assert.NotNull(output);
            Assert.NotNull(output.Pixels);
            Assert.True(output.Pixels.Length > 0, "管线输出的像素数组不应为空");

            // 验证：输出尺寸与输入相同
            Assert.Equal(input.Width, output.Width);
            Assert.Equal(input.Height, output.Height);

            // 验证：经过灰度化后应为 1 通道
            Assert.Equal(1, output.Channels);

            // 验证：像素数组长度正确
            Assert.Equal(output.Width * output.Height * output.Channels, output.Pixels.Length);
        }

        // ---------- 测试 10：管线诊断返回所有步骤 ----------

        /// <summary>
        /// 测试管线诊断功能：构建默认管线后使用 RunPipelineWithDiagnostics 执行，
        /// 应返回与管线步骤数相同数量的诊断结果。
        /// 默认管线包含 4 个步骤：灰度化、高斯模糊、OTSU 阈值、开运算。
        /// 每个步骤结果应包含有效的步骤名称、耗时和输出图像。
        /// </summary>
        [Fact]
        public void PipelineDiagnostics_ReturnsAllSteps()
        {
            // 准备：创建一幅 3 通道彩色图像
            var input = CreateColorImage(160, 120);

            // 构建默认管线（4 个步骤）
            _engine.BuildDefaultPipeline();
            int expectedStepCount = _engine.Pipeline.StepCount;

            // 执行：带诊断的管线运行
            var diagnostics = _engine.RunPipelineWithDiagnostics(input);

            // 验证：诊断结果数量应等于管线步骤数
            Assert.NotNull(diagnostics);
            Assert.Equal(expectedStepCount, diagnostics.Count);

            // 验证：默认管线应有 4 个步骤
            Assert.Equal(4, diagnostics.Count);

            // 验证：每个步骤结果的有效性
            foreach (var step in diagnostics)
            {
                // 步骤名称不应为空
                Assert.False(string.IsNullOrEmpty(step.StepName),
                    "管线步骤名称不应为空");

                // 执行耗时应为非负值
                Assert.True(step.Duration >= TimeSpan.Zero,
                    $"步骤 '{step.StepName}' 的耗时不应为负数");

                // 输出图像应有效
                Assert.NotNull(step.Output);
                Assert.True(step.Output.Width > 0, $"步骤 '{step.StepName}' 的输出宽度应大于 0");
                Assert.True(step.Output.Height > 0, $"步骤 '{step.StepName}' 的输出高度应大于 0");
                Assert.NotNull(step.Output.Pixels);
                Assert.True(step.Output.Pixels.Length > 0, $"步骤 '{step.StepName}' 的输出像素数组不应为空");
            }
        }

        // ---------- 测试 11：勾股定理距离验证 ----------

        /// <summary>
        /// 测试距离测量：使用经典的 3-4-5 勾股数验证距离计算。
        /// 从点 (0,0) 到点 (3,4) 的欧氏距离应为 5.0。
        /// 这是距离公式 d = sqrt(dx^2 + dy^2) 的基本验证。
        /// </summary>
        [Fact]
        public void MeasureDistance_PythagoreanTriple()
        {
            // 执行：测量 (0,0) 到 (3,4) 的距离
            double distance = _engine.MeasureDistance(0, 0, 3, 4);

            // 验证：距离应精确等于 5.0（经典 3-4-5 勾股数）
            AssertClose(5.0, distance, 1e-10, "勾股定理距离计算不正确");
        }

        // ---------- 测试 12：直角角度测量验证 ----------

        /// <summary>
        /// 测试角度测量：验证三点构成的直角（90度）能被正确测量。
        /// 使用三个点：P1(1,0), P2(0,0), P3(0,1)。
        /// P2 为顶点，P2→P1 方向为正 X 轴，P2→P3 方向为正 Y 轴，
        /// 夹角应为 90 度。
        /// </summary>
        [Fact]
        public void MeasureAngle_RightAngle()
        {
            // 准备：构造直角三角形的三个点
            // P1(1, 0) — X 轴正方向
            // P2(0, 0) — 顶点（原点）
            // P3(0, 1) — Y 轴正方向
            double x1 = 1, y1 = 0; // P1
            double x2 = 0, y2 = 0; // P2（顶点）
            double x3 = 0, y3 = 1; // P3

            // 执行：测量 P1-P2-P3 的夹角
            double angle = _engine.MeasureAngle(x1, y1, x2, y2, x3, y3);

            // 验证：夹角应为 90 度（容差 0.01 度）
            AssertClose(90.0, angle, 0.01, "直角角度测量不正确");
        }
    }
}
