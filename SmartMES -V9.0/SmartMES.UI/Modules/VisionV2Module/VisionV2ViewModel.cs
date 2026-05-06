using SmartMES.Core.Infrastructure;
using SmartMES.Core.Models;
using SmartMES.Modules.Vision;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace SmartMES.UI.Modules.VisionV2Module
{
    /// <summary>
    /// 视觉算法V2 ViewModel — 演示高级视觉处理子系统。
    /// 设计职责：
    /// 1) 管线式图像处理（灰度→高斯→OTSU→形态学）；
    /// 2) NCC模板匹配（归一化互相关 + 金字塔搜索）；
    /// 3) CCL连通域Blob分析（两遍扫描 + 并查集）；
    /// 4) 直方图分析与OTSU自适应阈值；
    /// 5) 测量工具（距离/角度/圆拟合Kasa/直线拟合）；
    /// 6) 9点仿射标定（最小二乘法求解2×3矩阵）。
    ///
    /// 技术要点：
    /// - VisionEngineV2 提供了高层门面方法（如 MeasureDistance/FitCircle/OtsuThreshold），
    ///   内部委托给对应静态工具类（MeasurementTools/HistogramAnalyzer），避免外部直接实例化静态类。
    /// - ImageData 使用 byte[] Pixels 存储像素，不依赖 WPF，可在后台线程执行。
    /// - CalibrationService.Calibrate 需要 List&lt;CalibrationPointPair&gt; 参数，返回 CalibrationData。
    /// </summary>
    public class VisionV2ViewModel : ViewModelBase
    {
        // VisionEngineV2：封装了所有视觉子系统的门面类
        private readonly VisionEngineV2 _engine = new();

        private string _statusText = "就绪";
        private string _pipelineResult = "未执行";
        private string _matchResult = "未执行";
        private string _blobResult = "未执行";
        private string _measureResult = "未执行";
        private string _calibResult = "未执行";
        private string _histogramInfo = "未分析";
        private bool _isBusy;
        private int _imageWidth = 200;
        private int _imageHeight = 200;

        public ObservableCollection<string> Logs { get; } = new();
        public ObservableCollection<string> PipelineSteps { get; } = new();

        /// <summary>状态栏。</summary>
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        /// <summary>管线执行结果。</summary>
        public string PipelineResult { get => _pipelineResult; set => SetProperty(ref _pipelineResult, value); }

        /// <summary>模板匹配结果。</summary>
        public string MatchResult { get => _matchResult; set => SetProperty(ref _matchResult, value); }

        /// <summary>Blob分析结果。</summary>
        public string BlobResult { get => _blobResult; set => SetProperty(ref _blobResult, value); }

        /// <summary>测量结果。</summary>
        public string MeasureResult { get => _measureResult; set => SetProperty(ref _measureResult, value); }

        /// <summary>标定结果。</summary>
        public string CalibResult { get => _calibResult; set => SetProperty(ref _calibResult, value); }

        /// <summary>直方图信息。</summary>
        public string HistogramInfo { get => _histogramInfo; set => SetProperty(ref _histogramInfo, value); }

        /// <summary>执行中标志。</summary>
        public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

        /// <summary>测试图像宽度。</summary>
        public int ImageWidth { get => _imageWidth; set => SetProperty(ref _imageWidth, value); }

        /// <summary>测试图像高度。</summary>
        public int ImageHeight { get => _imageHeight; set => SetProperty(ref _imageHeight, value); }

        public RelayCommand RunPipelineCommand { get; }
        public RelayCommand RunMatchCommand { get; }
        public RelayCommand RunBlobCommand { get; }
        public RelayCommand RunMeasureCommand { get; }
        public RelayCommand RunCalibCommand { get; }
        public RelayCommand RunHistogramCommand { get; }
        public RelayCommand RunAllCommand { get; }
        public RelayCommand ClearLogCommand { get; }

        /// <summary>创建视觉V2 ViewModel，初始化引擎默认管线和命令绑定。</summary>
        public VisionV2ViewModel()
        {
            // VisionEngineV2构造时会自动构建默认管线（灰度→高斯→OTSU→形态学开运算）
            RunPipelineCommand  = new RelayCommand(async _ => await RunPipelineAsync());
            RunMatchCommand     = new RelayCommand(async _ => await RunMatchAsync());
            RunBlobCommand      = new RelayCommand(async _ => await RunBlobAsync());
            RunMeasureCommand   = new RelayCommand(async _ => await RunMeasureAsync());
            RunCalibCommand     = new RelayCommand(async _ => await RunCalibAsync());
            RunHistogramCommand = new RelayCommand(async _ => await RunHistogramAsync());
            RunAllCommand       = new RelayCommand(async _ => await RunAllAsync());
            ClearLogCommand     = new RelayCommand(_ => Logs.Clear());

            // 管线步骤列表（与引擎 BuildDefaultPipeline 一致）
            PipelineSteps.Add("1. 灰度化 (Grayscale)");
            PipelineSteps.Add("2. 高斯模糊 (GaussianBlur 3x3)");
            PipelineSteps.Add("3. OTSU阈值 (自适应二值化)");
            PipelineSteps.Add("4. 形态学开运算 (去噪)");

            Log("视觉算法V2引擎初始化完成");
            Log($"子系统：ImageProcessor / MorphologyProcessor / HistogramAnalyzer");
            Log($"子系统：TemplateMatcher(NCC) / BlobAnalyzer(CCL) / MeasurementTools");
            Log($"子系统：CalibrationService(9点标定) / VisionPipeline(可配置管线)");
        }

        /// <summary>
        /// 生成测试图像（在暗底噪声上画一个亮圆斑，模拟工件）。
        /// 使用 ImageData.Pixels（byte[]），不依赖WPF，可在后台线程安全执行。
        /// </summary>
        private ImageData CreateTestImage()
        {
            var w = ImageWidth;
            var h = ImageHeight;
            var img = ImageData.Create(w, h, 1);
            var rnd = Random.Shared;

            // 背景噪声（灰度 20~50）
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    img.Pixels[y * w + x] = (byte)(rnd.Next(20, 50));

            // 中心亮圆斑（灰度 ~180，模拟目标工件）
            int cx = w / 2, cy = h / 2, r = Math.Min(w, h) / 4;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                    if (x >= 0 && x < w && y >= 0 && y < h && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                        img.Pixels[y * w + x] = (byte)(180 + rnd.Next(-10, 10));

            return img;
        }

        /// <summary>执行完整处理管线（灰度→高斯→OTSU→形态学）。</summary>
        private async Task RunPipelineAsync()
        {
            IsBusy = true;
            StatusText = "管线执行中...";
            Log("▶ 开始执行图像处理管线");
            try
            {
                var (result, diag) = await Task.Run(() =>
                {
                    var img = CreateTestImage();
                    var r = _engine.RunPipeline(img);
                    var d = _engine.RunPipelineWithDiagnostics(CreateTestImage());
                    return (r, d);
                });

                PipelineResult = $"管线完成 — 输出: {result.Width}x{result.Height}, {result.Channels}通道";
                Log($"  管线输出: {result.Width}x{result.Height}, 像素范围: [{result.Pixels.Min()}..{result.Pixels.Max()}]");
                Log($"  诊断模式: {diag.Count} 步骤完成");
                foreach (var step in diag)
                    Log($"    - {step.StepName}: {step.Output.Width}x{step.Output.Height}");
                StatusText = "管线完成";
            }
            catch (Exception ex)
            {
                PipelineResult = $"管线失败: {ex.Message}";
                Log($"  ✗ 管线异常: {ex.Message}");
                StatusText = "管线失败";
            }
            finally { IsBusy = false; }
        }

        /// <summary>执行NCC模板匹配（从图像中心提取模板，在原图中搜索）。</summary>
        private async Task RunMatchAsync()
        {
            IsBusy = true;
            StatusText = "模板匹配中...";
            Log("▶ 开始NCC模板匹配（归一化互相关 + 金字塔搜索）");
            try
            {
                var results = await Task.Run(() =>
                {
                    var image = CreateTestImage();
                    int tw = 30, th = 30;
                    var templateData = new byte[tw * th];
                    int cx = image.Width / 2, cy = image.Height / 2;

                    for (int y = 0; y < th; y++)
                        for (int x = 0; x < tw; x++)
                        {
                            int srcX = cx - tw / 2 + x;
                            int srcY = cy - th / 2 + y;
                            if (srcX >= 0 && srcX < image.Width && srcY >= 0 && srcY < image.Height)
                                templateData[y * tw + x] = image.Pixels[srcY * image.Width + srcX];
                        }
                    var template = new ImageData { Width = tw, Height = th, Channels = 1, Pixels = templateData };
                    return _engine.MatchTemplate(image, template);
                });

                if (results.Count > 0)
                {
                    var best = results[0];
                    MatchResult = $"匹配成功: ({best.X},{best.Y}) 得分={best.Score:F3}";
                    Log($"  最佳匹配: 位置=({best.X},{best.Y}), Score={best.Score:F4}");
                    Log($"  共找到 {results.Count} 个匹配位置");
                }
                else
                {
                    MatchResult = "未找到匹配";
                    Log("  未找到匹配位置");
                }
                StatusText = "匹配完成";
            }
            catch (Exception ex)
            {
                MatchResult = $"匹配失败: {ex.Message}";
                Log($"  ✗ 匹配异常: {ex.Message}");
                StatusText = "匹配失败";
            }
            finally { IsBusy = false; }
        }

        /// <summary>执行Blob连通域分析（先OTSU二值化，再CCL标记连通域）。</summary>
        private async Task RunBlobAsync()
        {
            IsBusy = true;
            StatusText = "Blob分析中...";
            Log("▶ 开始Blob分析（两遍CCL + 并查集 + 特征提取）");
            try
            {
                var blobs = await Task.Run(() =>
                {
                    var img = CreateTestImage();
                    var binary = _engine.OtsuThreshold(img);
                    return _engine.FindBlobs(binary);
                });

                BlobResult = $"检测到 {blobs.Count} 个连通域";
                Log($"  连通域数量: {blobs.Count}");
                foreach (var b in blobs.Take(5))
                    Log($"    Blob: 面积={b.Area}, 重心=({b.CenterX:F1},{b.CenterY:F1}), 矩形=({b.BoundX},{b.BoundY},{b.BoundWidth},{b.BoundHeight})");
                StatusText = "Blob分析完成";
            }
            catch (Exception ex)
            {
                BlobResult = $"Blob分析失败: {ex.Message}";
                Log($"  ✗ Blob异常: {ex.Message}");
                StatusText = "Blob分析失败";
            }
            finally { IsBusy = false; }
        }

        /// <summary>
        /// 执行测量工具演示。
        /// 使用 VisionEngineV2 门面方法（返回 double），内部委托给静态 MeasurementTools 类。
        /// </summary>
        private async Task RunMeasureAsync()
        {
            IsBusy = true;
            StatusText = "测量中...";
            Log("▶ 开始测量工具演示");
            try
            {
                var logs = await Task.Run(() =>
                {
                    var entries = new List<string>();

                    double dist = _engine.MeasureDistance(0, 0, 3, 4);
                    entries.Add($"  距离测量: (0,0)→(3,4) = {dist:F4} (勾股定理验证)");

                    double angle = _engine.MeasureAngle(1, 0, 0, 0, 0, 1);
                    entries.Add($"  角度测量: 直角 = {angle:F2}°");

                    var circlePoints = new List<(double X, double Y)>();
                    for (int i = 0; i < 36; i++)
                    {
                        double a = i * Math.PI * 2 / 36;
                        circlePoints.Add((50 + 25 * Math.Cos(a), 50 + 25 * Math.Sin(a)));
                    }
                    var (cx, cy, cr, cerr) = _engine.FitCircle(circlePoints);
                    entries.Add($"  圆拟合(Kasa): 中心=({cx:F2},{cy:F2}), 半径={cr:F2}, 误差={cerr:F4}");

                    var linePoints = new List<(double X, double Y)>
                        { (0, 1), (1, 3), (2, 5), (3, 7), (4, 9) };
                    var (slope, intercept, lerr) = _engine.FitLine(linePoints);
                    entries.Add($"  直线拟合: y = {slope:F3}x + {intercept:F3}, 误差={lerr:F4}");

                    return entries;
                });

                foreach (var entry in logs) Log(entry);
                MeasureResult = "测量完成 — 距离/角度/圆拟合/直线拟合";
                StatusText = "测量完成";
            }
            catch (Exception ex)
            {
                MeasureResult = $"测量失败: {ex.Message}";
                Log($"  ✗ 测量异常: {ex.Message}");
                StatusText = "测量失败";
            }
            finally { IsBusy = false; }
        }

        /// <summary>
        /// 执行9点仿射标定（最小二乘法求解2×3矩阵）。
        /// CalibrationService.Calibrate 接收 List&lt;CalibrationPointPair&gt; 并返回 CalibrationData。
        /// </summary>
        private async Task RunCalibAsync()
        {
            IsBusy = true;
            StatusText = "标定中...";
            Log("▶ 开始9点仿射标定（最小二乘法求解2x3矩阵）");
            try
            {
                var calibData = await Task.Run(() =>
                {
                    var calib = _engine.CalibrationService;
                    double scale = 0.05;
                    var rnd = Random.Shared;

                    var points = new List<CalibrationPointPair>();
                    for (int row = 0; row < 3; row++)
                    {
                        for (int col = 0; col < 3; col++)
                        {
                            double px = 100 + col * 200 + rnd.NextDouble() * 2 - 1;
                            double py = 100 + row * 200 + rnd.NextDouble() * 2 - 1;
                            double wx = col * 200 * scale;
                            double wy = row * 200 * scale;
                            points.Add(new CalibrationPointPair
                            {
                                PixelX = px, PixelY = py,
                                WorldX = wx, WorldY = wy
                            });
                        }
                    }
                    return calib.Calibrate(points);
                });

                CalibResult = $"标定完成 — 最大误差={calibData.MaxError:F4}mm, 平均误差={calibData.MeanError:F4}mm";
                Log($"  标定残差: Max={calibData.MaxError:F4}mm, Mean={calibData.MeanError:F4}mm");
                Log($"  标定点数: {calibData.PointCount}");
                Log($"  仿射矩阵已更新，可用于像素→世界坐标变换");
                StatusText = "标定完成";
            }
            catch (Exception ex)
            {
                CalibResult = $"标定失败: {ex.Message}";
                Log($"  ✗ 标定异常: {ex.Message}");
                StatusText = "标定失败";
            }
            finally { IsBusy = false; }
        }

        /// <summary>
        /// 执行直方图分析。
        /// 使用 VisionEngineV2 门面方法（ComputeHistogram/ComputeOtsuThreshold），
        /// 内部委托给静态 HistogramAnalyzer 类。
        /// </summary>
        private async Task RunHistogramAsync()
        {
            IsBusy = true;
            StatusText = "直方图分析中...";
            Log("▶ 开始直方图分析（OTSU阈值 + 均衡化）");
            try
            {
                var logs = await Task.Run(() =>
                {
                    var entries = new List<string>();
                    var img = CreateTestImage();

                    byte otsu = _engine.ComputeOtsuThreshold(img);
                    entries.Add($"  OTSU最佳阈值: {otsu}");

                    var histogram = _engine.ComputeHistogram(img);
                    int peakBin = 0, peakVal = 0;
                    for (int i = 0; i < histogram.Bins.Length; i++)
                        if (histogram.Bins[i] > peakVal) { peakVal = histogram.Bins[i]; peakBin = i; }
                    entries.Add($"  直方图峰值: bin={peakBin}, count={peakVal}");
                    entries.Add($"  均值={histogram.Mean:F2}, 标准差={histogram.StdDev:F2}");

                    var binarized = _engine.OtsuThreshold(img);
                    var binHist = _engine.ComputeHistogram(binarized);
                    entries.Add($"  二值化后: 均值={binHist.Mean:F2}, 标准差={binHist.StdDev:F2}");

                    return entries;
                });

                foreach (var entry in logs) Log(entry);
                HistogramInfo = "OTSU + 直方图分析完成";
                StatusText = "直方图分析完成";
            }
            catch (Exception ex)
            {
                HistogramInfo = $"直方图分析失败: {ex.Message}";
                Log($"  ✗ 直方图异常: {ex.Message}");
                StatusText = "直方图分析失败";
            }
            finally { IsBusy = false; }
        }

        /// <summary>运行所有算法演示（顺序执行全部6项）。</summary>
        private async Task RunAllAsync()
        {
            Log("══════════════════════════════════════");
            Log("▶▶ 一键运行全部视觉算法演示");
            Log("══════════════════════════════════════");

            try
            {
                await RunPipelineAsync();
                await RunHistogramAsync();
                await RunMatchAsync();
                await RunBlobAsync();
                await RunMeasureAsync();
                await RunCalibAsync();
            }
            catch (Exception ex)
            {
                Log($"✗ 运行中断: {ex.Message}");
            }

            Log("══════════════════════════════════════");
            Log("✓ 全部算法演示完成");
            StatusText = "全部完成";
        }

        /// <summary>追加日志（带时间戳，超过500条自动裁剪）。</summary>
        private void Log(string msg)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            if (Logs.Count > 500) Logs.RemoveAt(0);
            Logs.Add(entry);
        }
    }
}
