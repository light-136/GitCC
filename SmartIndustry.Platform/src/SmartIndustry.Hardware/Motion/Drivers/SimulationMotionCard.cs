// ============================================================
// 文件：SimulationMotionCard.cs
// 层级：硬件抽象层（Hardware Layer）> Motion > Drivers
// 职责：纯软件模拟运动控制卡，实现 IMotionCard 接口。
//       用于开发阶段无实体硬件时的功能验证和自动化测试。
//       生产环境替换为 LeisaiMotionCard、GuGaoMotionCard 等真实驱动实现类。
//
// 核心设计思路：
//   1. 每个轴独立一个 AxisSimState 存储其运动状态，
//      使用 ConcurrentDictionary 保证多线程并发读写安全。
//   2. 梯形速度曲线：将运动分为加速段/匀速段/减速段，
//      由 System.Threading.Timer（10ms周期）驱动位置更新。
//   3. 模拟回零：先以快速速度移向负限位，
//      触发负限位后慢速退出，然后将当前位置设为0。
//   4. 模拟限位开关：每个轴有正/负软件限位，超出时触发。
//   5. IO模拟：通过两个 Dictionary 分别存储 DI 和 DO 状态。
//
// 线程模型：
//   - _simulationTimer 在线程池线程上执行（每10ms一次）
//   - 所有轴状态通过 ConcurrentDictionary 线程安全访问
//   - 轴状态内部的属性更新使用 Interlocked 或 lock(_axisLock)
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using System.Collections.Concurrent;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Models;

namespace SmartIndustry.Hardware.Motion.Drivers
{
    /// <summary>
    /// 单轴模拟运动状态 — 存储一个轴的全部实时运动参数。
    /// 由 SimulationMotionCard 的 Timer 线程更新，AxisController 线程读取。
    /// 所有字段用 volatile 或在外部加锁访问。
    /// </summary>
    internal class AxisSimState
    {
        // 轴索引（不可变，构造后固定）
        public int Index { get; init; }

        // ---- 使能与回零状态 ----
        public bool IsEnabled { get; set; }
        public bool IsHomed { get; set; }

        // ---- 位置与速度（由 Timer 线程更新）----
        public double ActualPosition { get; set; }
        public double CommandPosition { get; set; }
        public double ActualVelocity { get; set; }

        // ---- 运动规划参数（由 StartMove 等指令写入）----
        public bool IsMoving { get; set; }
        public double StartPosition { get; set; }   // 本次运动起始位置
        public double TargetPosition { get; set; }   // 目标位置
        public double MaxVelocity { get; set; }       // 最大速度
        public double Acceleration { get; set; }      // 加速度
        public double Deceleration { get; set; }      // 减速度（新接口支持独立减速度）
        public double MoveStartTime { get; set; }    // 运动开始时间（Environment.TickCount64 ms）

        // ---- 梯形曲线时间参数（计算一次，Timer 中使用）----
        public double AccelTime { get; set; }         // 加速阶段时长（s）
        public double ConstTime { get; set; }         // 匀速阶段时长（s）
        public double DecelTime { get; set; }         // 减速阶段时长（s）
        public double TotalTime { get; set; }         // 总运动时长（s）
        public double PeakVelocity { get; set; }     // 实际达到的峰值速度（三角曲线时<MaxVelocity）
        public double Direction { get; set; } = 1.0; // +1=正向，-1=负向

        // ---- 限位状态 ----
        public double PositiveSoftLimit { get; set; } = 500.0;   // 正向软限位（mm）
        public double NegativeSoftLimit { get; set; } = -10.0;   // 负向软限位（mm）
        public bool PositiveLimitActive { get; set; }
        public bool NegativeLimitActive { get; set; }
        public bool HomeSensorActive { get; set; }

        // ---- 错误状态 ----
        public bool HasError { get; set; }
        public int ErrorCode { get; set; }

        // ---- 回零专用状态机 ----
        public HomingPhase HomingPhase { get; set; } = HomingPhase.None;
        public HomingConfig? HomingConfig { get; set; }

        // ---- 单轴锁（保护以上可变字段）----
        public readonly object Lock = new();
    }

    /// <summary>
    /// 回零阶段枚举，驱动轴的回零状态机
    /// </summary>
    internal enum HomingPhase
    {
        /// <summary>未回零</summary>
        None = 0,
        /// <summary>快速向负方向搜索原点传感器</summary>
        SearchSensor = 1,
        /// <summary>慢速正向退出传感器（精确定位）</summary>
        ExitSensor = 2,
        /// <summary>回零完成</summary>
        Completed = 3
    }

    /// <summary>
    /// 模拟运动控制卡 — 30轴纯软件仿真实现。
    ///
    /// 功能说明：
    ///   - 支持最多30轴的并发模拟运动
    ///   - 梯形速度曲线仿真（基于时间参数计算，Timer 10ms刷新位置）
    ///   - 模拟回零流程（两阶段：快速搜索→慢速退出）
    ///   - 正/负软件限位保护（超限自动停止）
    ///   - IO读写仿真（Dictionary 存储）
    ///   - 编码器位置反馈（= 指令位置 + 随机微小噪声）
    ///
    /// 使用方式：
    ///   var card = new SimulationMotionCard("SimCard0", 30);
    ///   await card.OpenCard();
    ///   card.EnableAxis(0);
    ///   card.StartMove(0, 100.0, 200.0, 500.0, 500.0);
    /// </summary>
    public sealed class SimulationMotionCard : IMotionCard, IDisposable
    {
        // ==================== 私有字段 ====================

        /// <summary>控制卡唯一标识</summary>
        private readonly string _cardId;

        /// <summary>支持的最大轴数</summary>
        private readonly int _maxAxisCount;

        /// <summary>各轴模拟状态（线程安全字典，Key=轴索引）</summary>
        private readonly ConcurrentDictionary<int, AxisSimState> _axisStates = new();

        /// <summary>数字输入/输出状态存储（Key=IO索引，统一地址空间）</summary>
        private readonly ConcurrentDictionary<int, bool> _digitalIo = new();

        /// <summary>模拟输入值存储（Key=IO索引）</summary>
        private readonly ConcurrentDictionary<int, double> _analogInputs = new();

        /// <summary>模拟输出值存储（Key=IO索引）</summary>
        private readonly ConcurrentDictionary<int, double> _analogOutputs = new();

        /// <summary>位置更新定时器（10ms 周期，驱动梯形速度曲线仿真）</summary>
        private Timer? _simulationTimer;

        /// <summary>模拟输入随机游走定时器（1s 周期）</summary>
        private Timer? _analogNoiseTimer;

        /// <summary>随机数生成器（编码器噪声和IO模拟）</summary>
        private readonly Random _random = new();

        /// <summary>是否已初始化</summary>
        private volatile bool _isInitialized;

        /// <summary>编码器随机噪声幅度（mm）</summary>
        private const double EncoderNoiseAmplitude = 0.0005;

        /// <summary>仿真定时器周期（ms）</summary>
        private const int SimTimerPeriodMs = 10;

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造模拟运动控制卡
        /// </summary>
        /// <param name="cardId">卡ID，唯一标识，例如"SimCard0"</param>
        /// <param name="maxAxisCount">最大轴数，默认30</param>
        public SimulationMotionCard(string cardId = "SimCard0", int maxAxisCount = 30)
        {
            _cardId = cardId;
            _maxAxisCount = maxAxisCount;
        }

        // ==================== IMotionCard 属性实现 ====================

        /// <inheritdoc/>
        public string CardId => _cardId;

        /// <inheritdoc/>
        public int MaxAxisCount => _maxAxisCount;

        /// <inheritdoc/>
        public bool IsInitialized => _isInitialized;

        // ==================== 生命周期 ====================

        /// <summary>
        /// 打开控制卡（对应 IMotionCard.OpenCard）：
        /// 1. 为每个轴创建 AxisSimState
        /// 2. 初始化 IO 字典（64路统一地址空间 + 8路AI/AO）
        /// 3. 启动位置更新定时器
        /// </summary>
        public Task<bool> OpenCard()
        {
            if (_isInitialized) return Task.FromResult(true);

            // 为所有轴初始化状态
            for (int i = 0; i < _maxAxisCount; i++)
            {
                var state = new AxisSimState
                {
                    Index = i,
                    ActualPosition = 0.0,
                    CommandPosition = 0.0,
                    // 模拟各轴不同的行程范围
                    PositiveSoftLimit = 500.0 + i * 10,
                    NegativeSoftLimit = -10.0
                };
                _axisStates[i] = state;
            }

            // 初始化64路数字IO（统一地址空间，0-31=DI，32-63=DO）
            for (int i = 0; i < 64; i++)
            {
                _digitalIo[i] = false;
            }

            // 初始化8路模拟IO
            for (int i = 0; i < 8; i++)
            {
                _analogInputs[i] = 0.0;
                _analogOutputs[i] = 0.0;
            }

            // 启动仿真定时器（10ms 周期）
            _simulationTimer = new Timer(SimulationTimerCallback, null,
                SimTimerPeriodMs, SimTimerPeriodMs);

            // 启动模拟输入随机游走定时器（1s 周期）
            _analogNoiseTimer = new Timer(AnalogNoiseCallback, null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            _isInitialized = true;
            return Task.FromResult(true);
        }

        /// <summary>
        /// 关闭模拟控制卡（对应 IMotionCard.CloseCard），停止定时器，释放资源
        /// </summary>
        public async Task CloseCard()
        {
            _isInitialized = false;

            // 停止并释放定时器（等待最后一次回调完成）
            if (_simulationTimer != null)
            {
                await _simulationTimer.DisposeAsync();
                _simulationTimer = null;
            }

            if (_analogNoiseTimer != null)
            {
                await _analogNoiseTimer.DisposeAsync();
                _analogNoiseTimer = null;
            }
        }

        // ==================== 轴参数设置 ====================

        /// <summary>
        /// 下载轴运动参数到仿真卡（对应 IMotionCard.SetAxisParam）。
        /// 将 MotionParameters 中的速度/加速度等参数写入轴状态，作为默认参数。
        /// </summary>
        /// <param name="axisIndex">轴索引（0-based）</param>
        /// <param name="parameters">运动参数对象</param>
        public void SetAxisParam(int axisIndex, MotionParameters parameters)
        {
            if (!TryGetAxis(axisIndex, out var state)) return;
            lock (state.Lock)
            {
                // 将参数写入轴状态（仿真中作为默认参数保存）
                state.MaxVelocity = parameters.MaxVelocity;
                state.Acceleration = parameters.Acceleration;
                state.Deceleration = parameters.Deceleration > 0
                    ? parameters.Deceleration
                    : parameters.Acceleration; // 未指定减速度时使用加速度值
            }
        }

        // ==================== 轴使能控制 ====================

        /// <inheritdoc/>
        public void EnableAxis(int axisIndex)
        {
            if (!TryGetAxis(axisIndex, out var state)) return;
            lock (state.Lock) { state.IsEnabled = true; }
        }

        /// <inheritdoc/>
        public void DisableAxis(int axisIndex)
        {
            if (!TryGetAxis(axisIndex, out var state)) return;
            lock (state.Lock)
            {
                state.IsEnabled = false;
                // 去使能同时停止运动
                state.IsMoving = false;
                state.ActualVelocity = 0;
            }
        }

        // ==================== 运动指令 ====================

        /// <summary>
        /// 启动单轴绝对/相对位置运动（对应 IMotionCard.StartMove）。
        /// 内部计算梯形速度曲线参数（加速时间/匀速时间/减速时间），
        /// Timer 回调根据经过时间计算当前应处于曲线的哪个阶段，更新位置。
        /// 支持独立的加速度和减速度（非对称梯形曲线）。
        /// </summary>
        /// <param name="axisIndex">轴索引</param>
        /// <param name="targetPosition">目标位置（mm）</param>
        /// <param name="velocity">运动速度（mm/s）</param>
        /// <param name="acceleration">加速度（mm/s²）</param>
        /// <param name="deceleration">减速度（mm/s²）</param>
        public void StartMove(int axisIndex, double targetPosition, double velocity,
            double acceleration, double deceleration)
        {
            if (!TryGetAxis(axisIndex, out var state)) return;
            lock (state.Lock)
            {
                if (!state.IsEnabled || state.HasError) return;

                double distance = targetPosition - state.ActualPosition;
                if (Math.Abs(distance) < 0.0001) return; // 已在目标位置

                // 计算梯形速度曲线参数（支持独立减速度）
                CalculateTrapezoidalParams(state, state.ActualPosition, targetPosition,
                    velocity, acceleration, deceleration);
                state.MoveStartTime = Environment.TickCount64;
                state.IsMoving = true;
                state.HomingPhase = HomingPhase.None;
            }
        }

        /// <inheritdoc/>
        public void StopMove(int axisIndex)
        {
            if (!TryGetAxis(axisIndex, out var state)) return;
            lock (state.Lock)
            {
                state.IsMoving = false;
                state.ActualVelocity = 0;
                state.CommandPosition = state.ActualPosition;
                state.HomingPhase = HomingPhase.None;
            }
        }

        /// <inheritdoc/>
        public void EmergencyStopAxis(int axisIndex)
        {
            if (!TryGetAxis(axisIndex, out var state)) return;
            lock (state.Lock)
            {
                state.IsMoving = false;
                state.ActualVelocity = 0;
                state.CommandPosition = state.ActualPosition;
                state.HomingPhase = HomingPhase.None;
                // 急停设错误标志（需要ClearAxisError才能重新使用）
                state.HasError = true;
                state.ErrorCode = 99; // 99=急停错误码
            }
        }

        /// <inheritdoc/>
        public void StartJog(int axisIndex, double velocity, double acceleration)
        {
            if (!TryGetAxis(axisIndex, out var state)) return;
            lock (state.Lock)
            {
                if (!state.IsEnabled || state.HasError) return;
                // 点动：目标设为软限位方向的边界，相当于无限运动直到 StopMove
                double jogTarget = velocity > 0
                    ? state.PositiveSoftLimit
                    : state.NegativeSoftLimit;
                CalculateTrapezoidalParams(state, state.ActualPosition, jogTarget,
                    Math.Abs(velocity), acceleration, acceleration);
                state.MoveStartTime = Environment.TickCount64;
                state.IsMoving = true;
            }
        }

        // ==================== 回零控制 ====================

        /// <summary>
        /// 启动模拟回零流程（对应 IMotionCard.StartHoming，两阶段状态机，由 Timer 驱动）：
        /// Phase1-SearchSensor：以快速速度向负方向运动，到达负软限位附近时模拟触发传感器
        /// Phase2-ExitSensor：慢速正方向退出传感器，完成后将当前位置设为0
        /// </summary>
        public void StartHoming(int axisIndex, HomingConfig config)
        {
            if (!TryGetAxis(axisIndex, out var state)) return;
            lock (state.Lock)
            {
                if (!state.IsEnabled) return;
                state.HomingConfig = config;
                state.HomingPhase = HomingPhase.SearchSensor;
                state.IsHomed = false;
                // 快速向负限位运动（模拟搜索传感器）
                double searchTarget = state.NegativeSoftLimit + 5.0; // 停在负限位+5mm处（模拟传感器位置）
                CalculateTrapezoidalParams(state, state.ActualPosition, searchTarget,
                    config.SearchVelocity, config.SearchVelocity * 2, config.SearchVelocity * 2);
                state.MoveStartTime = Environment.TickCount64;
                state.IsMoving = true;
            }
        }

        // ==================== 错误与归零 ====================

        /// <inheritdoc/>
        public void ClearAxisError(int axisIndex)
        {
            if (!TryGetAxis(axisIndex, out var state)) return;
            lock (state.Lock)
            {
                state.HasError = false;
                state.ErrorCode = 0;
                state.PositiveLimitActive = false;
                state.NegativeLimitActive = false;
            }
        }

        /// <inheritdoc/>
        public void SetZeroPosition(int axisIndex)
        {
            if (!TryGetAxis(axisIndex, out var state)) return;
            lock (state.Lock)
            {
                state.ActualPosition = 0;
                state.CommandPosition = 0;
            }
        }

        // ==================== 状态读取 ====================

        /// <summary>
        /// 读取编码器反馈位置（对应 IMotionCard.ReadPosition）。
        /// 在指令位置基础上叠加随机微小噪声，模拟真实编码器抖动。
        /// </summary>
        public double ReadPosition(int axisIndex)
        {
            if (!TryGetAxis(axisIndex, out var state)) return 0;
            lock (state.Lock)
            {
                // 模拟编码器噪声（±0.0005mm）
                double noise = (_random.NextDouble() - 0.5) * 2 * EncoderNoiseAmplitude;
                return state.ActualPosition + noise;
            }
        }

        /// <summary>
        /// 读取当前速度反馈（对应 IMotionCard.ReadVelocity）
        /// </summary>
        public double ReadVelocity(int axisIndex)
        {
            if (!TryGetAxis(axisIndex, out var state)) return 0;
            lock (state.Lock) { return state.ActualVelocity; }
        }

        /// <summary>
        /// 读取轴完整状态快照（对应 IMotionCard.ReadAxisStatus）
        /// </summary>
        public AxisStatus ReadAxisStatus(int axisIndex)
        {
            if (!TryGetAxis(axisIndex, out var state))
                return new AxisStatus { AxisIndex = axisIndex, HasError = true, ErrorCode = -1 };

            lock (state.Lock)
            {
                return new AxisStatus
                {
                    AxisIndex = axisIndex,
                    ActualPosition = state.ActualPosition,
                    CommandPosition = state.CommandPosition,
                    ActualVelocity = state.ActualVelocity,
                    IsEnabled = state.IsEnabled,
                    IsMoving = state.IsMoving,
                    IsHomed = state.IsHomed,
                    PositiveLimitActive = state.PositiveLimitActive,
                    NegativeLimitActive = state.NegativeLimitActive,
                    HomeSensorActive = state.HomeSensorActive,
                    HasError = state.HasError,
                    ErrorCode = state.ErrorCode,
                    Timestamp = DateTime.Now
                };
            }
        }

        // ==================== IO 操作 ====================

        /// <summary>
        /// 写数字输出（对应 IMotionCard.WriteIo，true=ON，false=OFF）。
        /// ioIndex 为 IO 地址，写操作仅对输出地址有效。
        /// </summary>
        public void WriteIo(int ioIndex, bool value)
            => _digitalIo[ioIndex] = value;

        /// <summary>
        /// 读数字输入当前状态（对应 IMotionCard.ReadIo）。
        /// ioIndex 为 IO 地址，读操作对输入/输出地址均有效。
        /// </summary>
        public bool ReadIo(int ioIndex)
            => _digitalIo.TryGetValue(ioIndex, out bool val) && val;

        /// <inheritdoc/>
        public double ReadAnalogInput(int ioIndex)
            => _analogInputs.TryGetValue(ioIndex, out double val) ? val : 0.0;

        /// <inheritdoc/>
        public void WriteAnalogOutput(int ioIndex, double value)
            => _analogOutputs[ioIndex] = Math.Clamp(value, 0.0, 10.0);

        // ==================== 多轴插补 ====================

        /// <summary>
        /// 启动多轴线性插补运动（对应 IMotionCard.StartInterpolation）。
        /// 仿真实现：将插补分解为各轴独立的比例速度运动，同步到达目标位置。
        /// 真实控制卡（如固高GTS）会在卡内进行硬件插补，这里用软件模拟等效行为。
        /// </summary>
        /// <param name="axisIndices">参与插补的轴索引数组</param>
        /// <param name="targetPositions">各轴目标位置（mm）</param>
        /// <param name="velocity">合成路径速度（mm/s）</param>
        /// <param name="acceleration">加速度（mm/s²）</param>
        public void StartInterpolation(int[] axisIndices, double[] targetPositions,
            double velocity, double acceleration)
        {
            if (axisIndices == null || targetPositions == null) return;
            if (axisIndices.Length != targetPositions.Length) return;
            if (axisIndices.Length == 0) return;

            // 计算各轴位移和总路径长度（用于按比例分配各轴速度）
            var deltas = new double[axisIndices.Length];
            double totalLength = 0;
            for (int i = 0; i < axisIndices.Length; i++)
            {
                if (!TryGetAxis(axisIndices[i], out var s)) continue;
                lock (s.Lock) { deltas[i] = targetPositions[i] - s.ActualPosition; }
                totalLength += deltas[i] * deltas[i];
            }
            totalLength = Math.Sqrt(totalLength);
            if (totalLength < 0.0001) return; // 各轴均已在目标位置

            // 对每个轴按路径方向余弦分配速度，同步启动运动
            for (int i = 0; i < axisIndices.Length; i++)
            {
                if (!TryGetAxis(axisIndices[i], out var state)) continue;
                // 各轴速度 = 合成速度 × 该轴方向余弦
                double axisVelocity = velocity * Math.Abs(deltas[i]) / totalLength;
                if (axisVelocity < 0.001) continue; // 该轴几乎不动，跳过

                lock (state.Lock)
                {
                    if (!state.IsEnabled || state.HasError) continue;
                    CalculateTrapezoidalParams(state, state.ActualPosition, targetPositions[i],
                        axisVelocity, acceleration, acceleration);
                    state.MoveStartTime = Environment.TickCount64;
                    state.IsMoving = true;
                    state.HomingPhase = HomingPhase.None;
                }
            }
        }

        // ==================== 测试辅助方法（非接口）====================

        /// <summary>
        /// 手动设置数字输入状态（用于测试和仿真场景）
        /// </summary>
        public void SetDigitalInput(int ioIndex, bool value)
            => _digitalIo[ioIndex] = value;

        /// <summary>
        /// 手动设置模拟输入值（用于测试和仿真场景）
        /// </summary>
        public void SetAnalogInput(int ioIndex, double value)
            => _analogInputs[ioIndex] = Math.Clamp(value, 0.0, 10.0);

        // ==================== 私有方法 ====================

        /// <summary>
        /// 仿真定时器回调（10ms周期）。
        /// 遍历所有轴，根据梯形速度曲线参数计算当前位置，并更新轴状态。
        /// 同时处理回零状态机的阶段转换。
        /// </summary>
        private void SimulationTimerCallback(object? state)
        {
            foreach (var axisState in _axisStates.Values)
            {
                UpdateAxisPosition(axisState);
            }
        }

        /// <summary>
        /// 模拟输入随机游走回调（1s周期）
        /// 模拟模拟量输入信号的自然变化（温度、压力等传感器波动）
        /// </summary>
        private void AnalogNoiseCallback(object? state)
        {
            for (int i = 0; i < 8; i++)
            {
                if (_analogInputs.TryGetValue(i, out double current))
                {
                    double noise = (_random.NextDouble() - 0.5) * 0.5; // ±0.25V 随机游走
                    _analogInputs[i] = Math.Clamp(current + noise, 0.0, 10.0);
                }
            }
        }

        /// <summary>
        /// 更新单个轴的位置状态（梯形速度曲线仿真核心逻辑）。
        /// 算法：
        ///   根据从运动开始经过的时间 t，判断当前处于哪个运动阶段：
        ///   阶段1（t &lt; AccelTime）：加速段，velocity = a * t
        ///   阶段2（AccelTime &lt;= t &lt; AccelTime+ConstTime）：匀速段
        ///   阶段3（t >= AccelTime+ConstTime）：减速段，velocity = peak_v - d*(t-t_decel_start)
        ///   超过 TotalTime：运动完成
        /// </summary>
        private void UpdateAxisPosition(AxisSimState axis)
        {
            lock (axis.Lock)
            {
                if (!axis.IsMoving) return;

                // 经过时间（秒）
                double elapsedMs = Environment.TickCount64 - axis.MoveStartTime;
                double t = elapsedMs / 1000.0;

                double newPosition;
                double newVelocity;

                if (t >= axis.TotalTime)
                {
                    // 运动完成，到达目标
                    newPosition = axis.TargetPosition;
                    newVelocity = 0;
                    axis.IsMoving = false;

                    // 处理回零阶段转换
                    HandleHomingPhaseTransition(axis);
                }
                else
                {
                    // 计算梯形曲线当前时刻的位置和速度（支持非对称加减速）
                    (newPosition, newVelocity) = CalculateTrapezoidalPositionVelocity(axis, t);
                }

                axis.ActualPosition = newPosition;
                axis.CommandPosition = newPosition;
                axis.ActualVelocity = newVelocity;

                // 检查软限位
                CheckSoftLimits(axis);
            }
        }

        /// <summary>
        /// 梯形速度曲线位置和速度计算（支持非对称加减速）。
        /// 加速段使用 acceleration，减速段使用 deceleration（存储在 Deceleration 字段）。
        /// 返回 (位置, 速度)
        /// </summary>
        private static (double position, double velocity) CalculateTrapezoidalPositionVelocity(
            AxisSimState axis, double t)
        {
            double s = axis.StartPosition;
            double dir = axis.Direction;
            double pv = axis.PeakVelocity;
            double a = axis.Acceleration;
            double d = axis.Deceleration > 0 ? axis.Deceleration : axis.Acceleration;

            double posFromStart;
            double velocity;

            if (t <= axis.AccelTime)
            {
                // 加速段：s = 0.5 * a * t²，v = a * t
                posFromStart = 0.5 * a * t * t;
                velocity = a * t;
            }
            else if (t <= axis.AccelTime + axis.ConstTime)
            {
                // 匀速段：s = s_accel + v_peak * (t - t_accel)
                double sAccel = 0.5 * a * axis.AccelTime * axis.AccelTime;
                double dtConst = t - axis.AccelTime;
                posFromStart = sAccel + pv * dtConst;
                velocity = pv;
            }
            else
            {
                // 减速段：从匀速段末尾开始减速（使用独立减速度）
                double sAccel = 0.5 * a * axis.AccelTime * axis.AccelTime;
                double sConst = pv * axis.ConstTime;
                double dtDecel = t - axis.AccelTime - axis.ConstTime;
                posFromStart = sAccel + sConst + pv * dtDecel - 0.5 * d * dtDecel * dtDecel;
                velocity = Math.Max(0, pv - d * dtDecel);
            }

            double position = s + dir * posFromStart;
            return (position, velocity * dir);
        }

        /// <summary>
        /// 计算梯形速度曲线的时间参数（加速时间、匀速时间、减速时间、总时间）。
        /// 支持独立的加速度和减速度（非对称梯形曲线）。
        /// 处理三角形速度曲线情况（距离不足以达到最大速度）。
        /// </summary>
        private static void CalculateTrapezoidalParams(
            AxisSimState state,
            double startPos,
            double targetPos,
            double velocity,
            double acceleration,
            double deceleration)
        {
            double distance = Math.Abs(targetPos - startPos);
            double maxV = velocity;
            double a = Math.Max(acceleration, 1.0);  // 防止除零
            double d = Math.Max(deceleration, 1.0);  // 防止除零

            // 加速到最大速度需要的距离：d_accel = v²/(2a)
            // 从最大速度减速到0需要的距离：d_decel = v²/(2d)
            double distAccel = (maxV * maxV) / (2 * a);
            double distDecel = (maxV * maxV) / (2 * d);

            double peakV;
            double accelTime, constTime, decelTime;

            if (distAccel + distDecel >= distance)
            {
                // 三角形速度曲线：距离不足以达到最大速度
                // 联立：0.5*a*ta² + 0.5*d*td² = distance，v_peak = a*ta = d*td
                // 解得：v_peak = sqrt(2 * distance * a * d / (a + d))
                peakV = Math.Sqrt(2.0 * distance * a * d / (a + d));
                accelTime = peakV / a;
                constTime = 0;
                decelTime = peakV / d;
            }
            else
            {
                // 梯形速度曲线：可以达到最大速度
                peakV = maxV;
                accelTime = peakV / a;
                decelTime = peakV / d;
                double distConst = distance - distAccel - distDecel;
                constTime = distConst / peakV;
            }

            state.StartPosition = startPos;
            state.TargetPosition = targetPos;
            state.MaxVelocity = velocity;
            state.Acceleration = a;
            state.Deceleration = d;
            state.PeakVelocity = peakV;
            state.AccelTime = accelTime;
            state.ConstTime = constTime;
            state.DecelTime = decelTime;
            state.TotalTime = accelTime + constTime + decelTime;
            state.Direction = targetPos >= startPos ? 1.0 : -1.0;
        }

        /// <summary>
        /// 检查软限位，超出时强制停止并标记限位状态
        /// </summary>
        private static void CheckSoftLimits(AxisSimState axis)
        {
            if (axis.ActualPosition >= axis.PositiveSoftLimit)
            {
                axis.ActualPosition = axis.PositiveSoftLimit;
                axis.CommandPosition = axis.PositiveSoftLimit;
                axis.PositiveLimitActive = true;
                axis.IsMoving = false;
                axis.ActualVelocity = 0;
            }
            else if (axis.ActualPosition <= axis.NegativeSoftLimit)
            {
                axis.ActualPosition = axis.NegativeSoftLimit;
                axis.CommandPosition = axis.NegativeSoftLimit;
                axis.NegativeLimitActive = true;
                axis.IsMoving = false;
                axis.ActualVelocity = 0;
            }
            else
            {
                axis.PositiveLimitActive = false;
                axis.NegativeLimitActive = false;
            }

            // 模拟原点传感器（在 -5mm ~ 0mm 之间时触发）
            axis.HomeSensorActive = axis.ActualPosition >= -5.0 && axis.ActualPosition <= 0.0;
        }

        /// <summary>
        /// 处理回零阶段的状态转换。
        /// 当某个阶段的运动完成后，自动触发下一阶段。
        /// 阶段1完成→阶段2（慢速退出传感器）
        /// 阶段2完成→回零完成（将当前位置设为0）
        /// </summary>
        private static void HandleHomingPhaseTransition(AxisSimState axis)
        {
            if (axis.HomingConfig == null) return;

            if (axis.HomingPhase == HomingPhase.SearchSensor)
            {
                // 快速搜索完成，切换到慢速退出
                axis.HomingPhase = HomingPhase.ExitSensor;
                double exitTarget = axis.HomingConfig.Offset; // 退到偏移位置
                double creepVel = axis.HomingConfig.CreepVelocity;
                CalculateTrapezoidalParams(axis, axis.ActualPosition, exitTarget,
                    creepVel, creepVel * 2, creepVel * 2);
                axis.MoveStartTime = Environment.TickCount64;
                axis.IsMoving = true;
            }
            else if (axis.HomingPhase == HomingPhase.ExitSensor)
            {
                // 慢速退出完成，回零结束
                axis.HomingPhase = HomingPhase.Completed;
                axis.IsHomed = true;
                axis.ActualPosition = 0;
                axis.CommandPosition = 0;
            }
        }

        /// <summary>
        /// 尝试获取轴状态，轴索引越界返回 false
        /// </summary>
        private bool TryGetAxis(int axisIndex, out AxisSimState state)
        {
            if (_axisStates.TryGetValue(axisIndex, out state!)) return true;
            state = null!;
            return false;
        }

        // ==================== IDisposable ====================

        /// <inheritdoc/>
        public void Dispose()
        {
            _simulationTimer?.Dispose();
            _analogNoiseTimer?.Dispose();
        }
    }
}
