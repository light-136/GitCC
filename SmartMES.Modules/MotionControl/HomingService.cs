// ============================================================
// 文件：HomingService.cs
// 用途：多策略回零服务 — 支持传感器/编码器脉冲/硬限位/当前位置四种回零方式
// 设计思路：
//   工业伺服系统在上电后需要执行"回零"操作，确定轴的绝对位置。
//   不同设备和场景需要不同的回零策略：
//   1. SensorBased（传感器回零）：快速搜索原点传感器，反向慢速精确定位
//   2. IndexPulse（编码器脉冲）：先找传感器，再找编码器Z脉冲，精度最高
//   3. HardStop（硬限位）：慢速撞限位检测堵转，适合无传感器场景
//   4. CurrentPosition（当前位置）：直接设当前位置为零，用于已知位置的场景
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 回零状态枚举 — 描述回零过程中的各个阶段。
    /// </summary>
    public enum HomingPhase
    {
        /// <summary>空闲，未开始回零。</summary>
        Idle,
        /// <summary>快速搜索原点传感器。</summary>
        FastSearch,
        /// <summary>反向慢速离开传感器。</summary>
        SlowRetract,
        /// <summary>搜索编码器Z脉冲（仅IndexPulse策略）。</summary>
        IndexSearch,
        /// <summary>慢速撞限位（仅HardStop策略）。</summary>
        SlowApproach,
        /// <summary>堵转后后退（仅HardStop策略）。</summary>
        Retract,
        /// <summary>回零完成。</summary>
        Completed,
        /// <summary>回零失败。</summary>
        Failed
    }

    /// <summary>
    /// 回零结果 — 封装回零操作的结果信息。
    /// </summary>
    public class HomingResult
    {
        /// <summary>是否成功。</summary>
        public bool Success { get; set; }

        /// <summary>回零后的位置（mm）。</summary>
        public double FinalPosition { get; set; }

        /// <summary>回零耗时（毫秒）。</summary>
        public double ElapsedMs { get; set; }

        /// <summary>错误信息（失败时）。</summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 多策略回零服务 — 根据配置选择不同的回零算法执行回零操作。
    ///
    /// 设计要点：
    ///   - 所有策略都是异步的，支持取消
    ///   - 传感器状态通过回调函数模拟（实际项目中由I/O驱动提供）
    ///   - 每种策略都有详细的阶段反馈
    /// </summary>
    public class HomingService
    {
        // 轴控制器引用
        private readonly AxisController _axis;

        // 传感器状态回调：返回true表示传感器被触发
        // 实际项目中这里会连接到真实I/O，仿真模式下用位置模拟
        private Func<bool>? _sensorCallback;

        // 编码器Z脉冲回调：返回true表示检测到Z脉冲
        private Func<bool>? _indexPulseCallback;

        /// <summary>当前回零阶段。</summary>
        public HomingPhase CurrentPhase { get; private set; } = HomingPhase.Idle;

        /// <summary>日志事件 — 用于UI显示回零过程。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>阶段变更事件。</summary>
        public event EventHandler<HomingPhase>? PhaseChanged;

        /// <summary>
        /// 构造函数 — 需要关联一个轴控制器。
        /// </summary>
        /// <param name="axis">要执行回零的轴。</param>
        public HomingService(AxisController axis)
        {
            _axis = axis ?? throw new ArgumentNullException(nameof(axis));
        }

        /// <summary>
        /// 设置传感器回调（用于仿真或实际I/O接口）。
        /// </summary>
        public void SetSensorCallback(Func<bool> callback)
        {
            _sensorCallback = callback;
        }

        /// <summary>
        /// 设置编码器Z脉冲回调。
        /// </summary>
        public void SetIndexPulseCallback(Func<bool> callback)
        {
            _indexPulseCallback = callback;
        }

        /// <summary>
        /// 执行回零操作 — 根据配置自动选择回零策略。
        /// </summary>
        /// <param name="config">回零配置（策略、速度、超时等）。</param>
        /// <param name="ct">取消令牌，允许外部中止回零。</param>
        /// <returns>回零结果。</returns>
        public async Task<HomingResult> ExecuteHomingAsync(HomingConfig config, CancellationToken ct = default)
        {
            var startTime = DateTime.Now;
            Log($"[回零] 轴 {_axis.AxisName} 开始回零，策略={config.Strategy}");

            try
            {
                HomingResult result = config.Strategy switch
                {
                    HomingStrategy.SensorBased => await HomeSensorBasedAsync(config, ct),
                    HomingStrategy.IndexPulse => await HomeIndexPulseAsync(config, ct),
                    HomingStrategy.HardStop => await HomeHardStopAsync(config, ct),
                    HomingStrategy.CurrentPosition => HomeCurrentPosition(config),
                    _ => new HomingResult { Success = false, ErrorMessage = "未知的回零策略" }
                };

                result.ElapsedMs = (DateTime.Now - startTime).TotalMilliseconds;
                SetPhase(result.Success ? HomingPhase.Completed : HomingPhase.Failed);

                Log($"[回零] 轴 {_axis.AxisName} 回零{(result.Success ? "成功" : "失败")}，" +
                    $"位置={result.FinalPosition:F3}mm，耗时={result.ElapsedMs:F0}ms");

                return result;
            }
            catch (OperationCanceledException)
            {
                SetPhase(HomingPhase.Failed);
                return new HomingResult
                {
                    Success = false,
                    ErrorMessage = "回零操作被取消",
                    ElapsedMs = (DateTime.Now - startTime).TotalMilliseconds
                };
            }
        }

        // ========== 策略1：传感器回零 ==========
        // 流程：快速负方向搜索 → 触发传感器 → 反向慢速离开 → 设置零点
        private async Task<HomingResult> HomeSensorBasedAsync(HomingConfig config, CancellationToken ct)
        {
            double fastSpeed = config.FastSpeed > 0 ? config.FastSpeed : 100;
            double slowSpeed = config.SlowSpeed > 0 ? config.SlowSpeed : 10;
            double timeout = config.TimeoutMs > 0 ? config.TimeoutMs : 30000;

            // 第一阶段：快速负方向搜索原点传感器
            SetPhase(HomingPhase.FastSearch);
            Log($"[回零] 快速搜索，速度={fastSpeed}mm/s，方向=负");

            _axis.Velocity = fastSpeed;
            _axis.MoveTo(_axis.Position - 10000); // 向负方向移动一个大距离

            bool sensorFound = await WaitForConditionAsync(
                () => _sensorCallback?.Invoke() ?? SimulateSensor(config),
                timeout, ct);

            if (!sensorFound)
            {
                _axis.Stop();
                return new HomingResult { Success = false, ErrorMessage = "未找到原点传感器（超时）" };
            }

            _axis.Stop();
            await Task.Delay(100, ct); // 等待轴停稳

            // 第二阶段：反向慢速离开传感器边缘
            SetPhase(HomingPhase.SlowRetract);
            Log($"[回零] 慢速反向搜索，速度={slowSpeed}mm/s");

            _axis.Velocity = slowSpeed;
            _axis.MoveTo(_axis.Position + 500); // 反向慢速移动

            bool sensorCleared = await WaitForConditionAsync(
                () => !(_sensorCallback?.Invoke() ?? SimulateSensor(config)),
                timeout, ct);

            if (!sensorCleared)
            {
                _axis.Stop();
                return new HomingResult { Success = false, ErrorMessage = "无法离开传感器边缘（超时）" };
            }

            _axis.Stop();
            await Task.Delay(50, ct);

            // 设置零点
            double homePos = config.HomeOffset;
            _axis.Position = homePos;
            _axis.IsHomed = true;

            return new HomingResult { Success = true, FinalPosition = homePos };
        }

        // ========== 策略2：编码器脉冲回零 ==========
        // 流程：先找传感器 → 再找编码器Z脉冲 → 精度最高
        private async Task<HomingResult> HomeIndexPulseAsync(HomingConfig config, CancellationToken ct)
        {
            double fastSpeed = config.FastSpeed > 0 ? config.FastSpeed : 100;
            double slowSpeed = config.SlowSpeed > 0 ? config.SlowSpeed : 5;
            double timeout = config.TimeoutMs > 0 ? config.TimeoutMs : 30000;

            // 第一阶段：快速搜索传感器（与传感器回零相同）
            SetPhase(HomingPhase.FastSearch);
            Log("[回零] 编码器脉冲模式：先快速搜索传感器");

            _axis.Velocity = fastSpeed;
            _axis.MoveTo(_axis.Position - 10000);

            bool sensorFound = await WaitForConditionAsync(
                () => _sensorCallback?.Invoke() ?? SimulateSensor(config),
                timeout, ct);

            if (!sensorFound)
            {
                _axis.Stop();
                return new HomingResult { Success = false, ErrorMessage = "未找到原点传感器" };
            }

            _axis.Stop();
            await Task.Delay(100, ct);

            // 第二阶段：慢速搜索编码器Z脉冲
            SetPhase(HomingPhase.IndexSearch);
            Log($"[回零] 搜索编码器Z脉冲，速度={slowSpeed}mm/s");

            _axis.Velocity = slowSpeed;
            _axis.MoveTo(_axis.Position + 100); // 慢速正向移动

            bool indexFound = await WaitForConditionAsync(
                () => _indexPulseCallback?.Invoke() ?? SimulateIndexPulse(),
                timeout, ct);

            if (!indexFound)
            {
                _axis.Stop();
                return new HomingResult { Success = false, ErrorMessage = "未找到编码器Z脉冲" };
            }

            _axis.Stop();
            await Task.Delay(50, ct);

            double homePos = config.HomeOffset;
            _axis.Position = homePos;
            _axis.IsHomed = true;

            return new HomingResult { Success = true, FinalPosition = homePos };
        }

        // ========== 策略3：硬限位回零 ==========
        // 流程：慢速负方向 → 检测堵转（位置不变化）→ 后退少量 → 设零点
        private async Task<HomingResult> HomeHardStopAsync(HomingConfig config, CancellationToken ct)
        {
            double slowSpeed = config.SlowSpeed > 0 ? config.SlowSpeed : 10;
            double timeout = config.TimeoutMs > 0 ? config.TimeoutMs : 30000;
            double retractDistance = 2.0; // 堵转后后退距离（mm）
            double stallThreshold = 0.01; // 堵转判定阈值（mm）

            // 第一阶段：慢速负方向运动
            SetPhase(HomingPhase.SlowApproach);
            Log($"[回零] 硬限位模式：慢速接近，速度={slowSpeed}mm/s");

            _axis.Velocity = slowSpeed;
            _axis.MoveTo(_axis.Position - 10000);

            // 检测堵转：连续3次采样位置不变
            double lastPos = _axis.Position;
            int stallCount = 0;
            var startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeout)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(50, ct);

                double currentPos = _axis.Position;
                if (Math.Abs(currentPos - lastPos) < stallThreshold)
                {
                    stallCount++;
                    if (stallCount >= 3) // 连续3次位置不变 = 堵转
                    {
                        Log("[回零] 检测到堵转");
                        break;
                    }
                }
                else
                {
                    stallCount = 0;
                }
                lastPos = currentPos;
            }

            if (stallCount < 3)
            {
                _axis.Stop();
                return new HomingResult { Success = false, ErrorMessage = "硬限位回零超时，未检测到堵转" };
            }

            _axis.Stop();
            await Task.Delay(100, ct);

            // 第二阶段：后退少量距离
            SetPhase(HomingPhase.Retract);
            Log($"[回零] 后退 {retractDistance}mm");

            _axis.Velocity = slowSpeed;
            _axis.MoveTo(_axis.Position + retractDistance);

            await WaitForConditionAsync(
                () => _axis.State == AxisState.Idle,
                5000, ct);

            double homePos = config.HomeOffset;
            _axis.Position = homePos;
            _axis.IsHomed = true;

            return new HomingResult { Success = true, FinalPosition = homePos };
        }

        // ========== 策略4：当前位置回零 ==========
        // 直接将当前位置设为零点+偏移量，即时完成
        private HomingResult HomeCurrentPosition(HomingConfig config)
        {
            Log($"[回零] 当前位置回零，偏移={config.HomeOffset}mm");

            _axis.Position = config.HomeOffset;
            _axis.IsHomed = true;

            return new HomingResult { Success = true, FinalPosition = config.HomeOffset };
        }

        // ========== 辅助方法 ==========

        /// <summary>
        /// 等待条件满足 — 带超时的轮询等待。
        /// </summary>
        private async Task<bool> WaitForConditionAsync(
            Func<bool> condition, double timeoutMs, CancellationToken ct)
        {
            var start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                if (condition()) return true;
                await Task.Delay(10, ct); // 10ms轮询周期
            }
            return false;
        }

        /// <summary>
        /// 模拟传感器触发 — 当轴位置接近零点时认为传感器触发。
        /// 仿真模式下使用，实际硬件通过回调函数提供真实传感器状态。
        /// </summary>
        private bool SimulateSensor(HomingConfig config)
        {
            return Math.Abs(_axis.Position - config.HomeOffset) < 5.0;
        }

        /// <summary>
        /// 模拟编码器Z脉冲 — 每整数毫米位置模拟一个Z脉冲。
        /// </summary>
        private bool SimulateIndexPulse()
        {
            double pos = _axis.Position;
            return Math.Abs(pos - Math.Round(pos)) < 0.05;
        }

        /// <summary>
        /// 更新回零阶段并触发事件。
        /// </summary>
        private void SetPhase(HomingPhase phase)
        {
            CurrentPhase = phase;
            PhaseChanged?.Invoke(this, phase);
        }

        /// <summary>
        /// 发送日志消息。
        /// </summary>
        private void Log(string message)
        {
            MessageLogged?.Invoke(this, message);
        }
    }
}
