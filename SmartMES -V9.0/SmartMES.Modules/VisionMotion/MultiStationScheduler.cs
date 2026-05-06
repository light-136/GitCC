using SmartMES.Modules.MotionControl;
using SmartMES.Modules.Vision;

namespace SmartMES.Modules.VisionMotion
{
    // ============================================================
    // 视觉+运动协同调度样例
    // 设备状态机：Idle->Positioning->Capturing->Detecting->Acting->Done
    // 多工位并行调度（3个工位同时独立运行）
    // ============================================================

    public enum StationState
    {
        Idle, Positioning, Capturing, Detecting, ActingOK, ActingNG, Done, Error
    }

    public class StationResult
    {
        public int     StationId   { get; set; }
        public bool    IsPass      { get; set; }
        public string  Message     { get; set; } = string.Empty;
        public double  CycleTimeMs { get; set; }
    }

    /// <summary>
    /// 单工位调度器 — 内部状态机
    /// 流程：定位(X/Y轴) → 拍照(模拟) → 视觉检测 → 合格/NG处理
    /// </summary>
    public class StationScheduler
    {
        public int     StationId   { get; }
        public string  Name        { get; }
        public StationState State  { get; private set; } = StationState.Idle;
        public string  Log         { get; private set; } = string.Empty;
        public int     OkCount     { get; private set; }
        public int     NgCount     { get; private set; }

        private readonly AxisController _xAxis;
        private readonly AxisController _yAxis;
        private readonly AxisController _zAxis;
        private readonly double[]       _pickPositions; // [x, y, z]
        private readonly Random         _rnd = new();

        public event EventHandler<string>? LogEmitted;
        public event EventHandler<StationResult>? CycleCompleted;
        public event EventHandler<StationState>? StateChanged;

        /// <summary>
        /// 自动补齐：StationScheduler 方法说明。
        /// </summary>
        public StationScheduler(
            int id, AxisController x, AxisController y, AxisController z,
            double[] pickPos)
        {
            StationId      = id;
            Name           = $"工位{id}";
            _xAxis         = x;
            _yAxis         = y;
            _zAxis         = z;
            _pickPositions = pickPos;
        }

        /// <summary>
        /// 自动补齐：SetState 方法说明。
        /// </summary>
        private void SetState(StationState s)
        {
            State = s;
            StateChanged?.Invoke(this, s);
            Emit($"[{Name}] → {s}");
        }

        /// <summary>
        /// 自动补齐：Emit 方法说明。
        /// </summary>
        private void Emit(string msg)
        {
            Log = msg;
            LogEmitted?.Invoke(this, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
        }

        /// <summary>执行一个完整检测周期</summary>
        public async Task<StationResult> RunCycleAsync(
            CancellationToken ct = default)
        {
            var start = DateTime.Now;
            try
            {
                // 1. 定位
                SetState(StationState.Positioning);
                _xAxis.MoveTo(_pickPositions[0] + _rnd.NextDouble() * 5);
                _yAxis.MoveTo(_pickPositions[1] + _rnd.NextDouble() * 5);
                _zAxis.MoveTo(50);
                await WaitAxesAsync(ct, _xAxis, _yAxis, _zAxis);
                await Task.Delay(100, ct);

                // 2. 拍照
                SetState(StationState.Capturing);
                await Task.Delay(200, ct); // 模拟曝光
                bool hasDefect = _rnd.NextDouble() < 0.2;
                var (pixels, pw, ph) = VisionEngine.GenerateWorkpiecePixels(320, 240, hasDefect);
                Emit($"[{Name}] 拍照完成，疑似缺陷: {hasDefect}");

                // 3. 视觉检测（使用线程安全的像素数组版本）
                SetState(StationState.Detecting);
                var result = await Task.Run(() => VisionEngine.InspectPixels(pixels, pw, ph, 70, 0.004), ct);
                bool isOk = result.Result == DetectionResult.OK;
                Emit($"[{Name}] 检测: {result.Result} — {result.Message}");

                // 4. 执行动作
                if (isOk)
                {
                    SetState(StationState.ActingOK);
                    // 合格品：移到放置位
                    _xAxis.MoveTo(_pickPositions[0] + 100);
                    _yAxis.MoveTo(_pickPositions[1] + 100);
                    _zAxis.MoveTo(10);
                    await WaitAxesAsync(ct, _xAxis, _yAxis, _zAxis);
                    OkCount++;
                }
                else
                {
                    SetState(StationState.ActingNG);
                    // NG品：移到废品箱
                    _xAxis.MoveTo(_pickPositions[0] - 80);
                    _yAxis.MoveTo(_pickPositions[1] - 50);
                    await WaitAxesAsync(ct, _xAxis, _yAxis);
                    NgCount++;
                }

                // 5. 复位
                _zAxis.MoveTo(80);
                SetState(StationState.Done);
                await Task.Delay(150, ct);
                SetState(StationState.Idle);

                var cycleResult = new StationResult
                {
                    StationId   = StationId,
                    IsPass      = isOk,
                    Message     = result.Message,
                    CycleTimeMs = (DateTime.Now - start).TotalMilliseconds
                };
                CycleCompleted?.Invoke(this, cycleResult);
                return cycleResult;
            }
            catch (OperationCanceledException)
            {
                SetState(StationState.Idle);
                return new StationResult { StationId=StationId, Message="已取消" };
            }
            catch (Exception ex)
            {
                SetState(StationState.Error);
                Emit($"[{Name}] 异常: {ex.Message}");
                return new StationResult { StationId=StationId, Message=$"错误: {ex.Message}" };
            }
        }

        /// <summary>
        /// 自动补齐：WaitAxesAsync 方法说明。
        /// </summary>
        private static async Task WaitAxesAsync(
            CancellationToken ct, params AxisController[] axes)
        {
            await Task.Run(() =>
            {
                while (axes.Any(a => a.State == AxisState.Running))
                    System.Threading.Thread.Sleep(20);
            }, ct);
        }
    }

    /// <summary>多工位并行调度器</summary>
    public class MultiStationScheduler
    {
        private readonly MultiAxisController _mc = new();
        public  List<StationScheduler> Stations { get; } = new();
        public  IReadOnlyDictionary<string,AxisController> Axes => _mc.Axes;

        private CancellationTokenSource _cts = new();
        public bool IsRunning { get; private set; }

        public event EventHandler<string>? LogEmitted;

        /// <summary>
        /// 自动补齐：MultiStationScheduler 方法说明。
        /// </summary>
        public MultiStationScheduler()
        {
            // 每个工位独立XYZ三轴
            for (int i = 1; i <= 3; i++)
            {
                _mc.AddAxis($"X{i}", 300, 1200);
                _mc.AddAxis($"Y{i}", 300, 1200);
                _mc.AddAxis($"Z{i}", 150, 600);
            }
            _mc.MessageLogged += (_, m) => LogEmitted?.Invoke(this, m);

            // 每个工位配不同的取料位
            var picks = new[] {
                new double[] { 50,  50,  20 },
                new double[] { 200, 50,  20 },
                new double[] { 350, 50,  20 },
            };
            for (int i = 1; i <= 3; i++)
            {
                var st = new StationScheduler(i,
                    _mc.Axes[$"X{i}"], _mc.Axes[$"Y{i}"], _mc.Axes[$"Z{i}"],
                    picks[i-1]);
                st.LogEmitted      += (_, m) => LogEmitted?.Invoke(this, m);
                Stations.Add(st);
            }
        }

        /// <summary>启动连续生产（每个工位独立循环N次）</summary>
        public async Task RunProductionAsync(int cycles = 5)
        {
            _cts = new CancellationTokenSource();
            IsRunning = true;
            LogEmitted?.Invoke(this, $"[调度器] 开始生产，{Stations.Count}个工位×{cycles}周期");

            // 所有工位并行运行
            var tasks = Stations.Select(async st =>
            {
                for (int c = 0; c < cycles; c++)
                {
                    if (_cts.Token.IsCancellationRequested) break;
                    await st.RunCycleAsync(_cts.Token);
                    await Task.Delay(100, _cts.Token).ContinueWith(_ => { });
                }
            });

            await Task.WhenAll(tasks);
            IsRunning = false;
            LogEmitted?.Invoke(this, "[调度器] 生产完成");
        }

        /// <summary>
        /// 自动补齐：Stop 方法说明。
        /// </summary>
        public void Stop()
        {
            _cts.Cancel();
            _mc.EmergencyStop();
            IsRunning = false;
        }

        /// <summary>
        /// 自动补齐：Reset 方法说明。
        /// </summary>
        public void Reset() => _mc.ResetAll();
    }
}
