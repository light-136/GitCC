// ============================================================
// 文件：SimulationMotionCard.cs
// 用途：模拟运动控制卡驱动
// 设计思路：
//   提供完整的运动控制模拟，无需真实硬件即可运行和学习。
//   模拟真实轴运动行为：加速→匀速→减速→到位。
//   每个轴独立线程运行运动计算，模拟实时性。
//
//   核心类：
//   - SimulationMotionCard：模拟控制卡，管理多个轴
//   - SimulationAxisController：模拟单轴控制器
//   - MotionSimulator：运动仿真引擎（梯形速度曲线）
//
//   扩展说明：
//   替换为真实控制卡只需实现 IMotionCard 和 IAxisController 接口。
//   例如：EtherCATMotionCard、GaoGongMotionCard 等。
// ============================================================

using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.Hardware.Motion.Drivers
{
    /// <summary>
    /// 模拟运动控制卡 — 实现 IMotionCard 接口。
    /// 每张卡最多支持32轴，可创建多张卡支持更多轴。
    /// </summary>
    public class SimulationMotionCard : IMotionCard
    {
        private readonly Dictionary<int, SimulationAxisController> _axes = new();
        private bool _isInitialized;

        /// <summary>控制卡ID</summary>
        public int CardId { get; }

        /// <summary>控制卡类型</summary>
        public MotionCardType CardType => MotionCardType.Simulation;

        /// <summary>最大轴数</summary>
        public int MaxAxisCount => 32;

        public SimulationMotionCard(int cardId)
        {
            CardId = cardId;
        }

        /// <summary>
        /// 初始化控制卡 — 创建所有轴的模拟控制器。
        /// </summary>
        public Task<bool> InitializeAsync()
        {
            if (_isInitialized) return Task.FromResult(true);
            _isInitialized = true;
            return Task.FromResult(true);
        }

        /// <summary>
        /// 配置一个轴 — 根据AxisConfig创建对应的模拟控制器。
        /// </summary>
        public void ConfigureAxis(AxisConfig config)
        {
            var controller = new SimulationAxisController(config);
            _axes[config.CardAxisIndex] = controller;
        }

        /// <summary>
        /// 获取指定轴的控制器。
        /// </summary>
        public IAxisController GetAxis(int axisIndex)
        {
            if (!_axes.ContainsKey(axisIndex))
            {
                // 自动创建默认配置的轴
                var config = new AxisConfig
                {
                    AxisId = axisIndex,
                    Name = $"Axis_{axisIndex}",
                    CardId = CardId,
                    CardAxisIndex = axisIndex
                };
                ConfigureAxis(config);
            }
            return _axes[axisIndex];
        }

        /// <summary>
        /// 直线插补运动 — 多轴同步到达目标位置。
        /// </summary>
        public async Task<bool> LinearMoveAsync(int[] axisIndices, double[] positions, double velocity,
            CancellationToken cancellationToken = default)
        {
            // 计算所有轴需要走的距离
            var distances = new double[axisIndices.Length];
            double maxDistance = 0;

            for (int i = 0; i < axisIndices.Length; i++)
            {
                var axis = _axes[axisIndices[i]];
                distances[i] = Math.Abs(positions[i] - axis.Status.Position);
                maxDistance = Math.Max(maxDistance, distances[i]);
            }

            if (maxDistance < 0.001) return true;

            // 计算运动时间（所有轴同时到达 → 最长距离的轴以设定速度运行，其余轴降速）
            var totalTime = maxDistance / velocity;

            // 同时启动所有轴运动
            var tasks = new List<Task<bool>>();
            for (int i = 0; i < axisIndices.Length; i++)
            {
                var axisVelocity = distances[i] / totalTime; // 按比例计算每轴速度
                var axis = _axes[axisIndices[i]];
                tasks.Add(axis.MoveAbsoluteAsync(positions[i], axisVelocity,
                    axis.Config.MaxAcceleration, axis.Config.MaxDeceleration, cancellationToken));
            }

            var results = await Task.WhenAll(tasks);
            return results.All(r => r);
        }

        /// <summary>
        /// 圆弧插补运动。
        /// </summary>
        public async Task<bool> ArcMoveAsync(int axisX, int axisY,
            double centerX, double centerY, double endX, double endY,
            double velocity, bool clockwise, CancellationToken cancellationToken = default)
        {
            // 简化实现：将圆弧离散为多段直线
            var xAxis = _axes[axisX];
            var yAxis = _axes[axisY];

            var startX = xAxis.Status.Position;
            var startY = yAxis.Status.Position;

            var radius = Math.Sqrt(Math.Pow(startX - centerX, 2) + Math.Pow(startY - centerY, 2));
            var startAngle = Math.Atan2(startY - centerY, startX - centerX);
            var endAngle = Math.Atan2(endY - centerY, endX - centerX);

            // 离散为36段
            var segments = 36;
            var angleStep = (endAngle - startAngle) / segments;
            if (clockwise) angleStep = -Math.Abs(angleStep);

            for (int i = 1; i <= segments; i++)
            {
                if (cancellationToken.IsCancellationRequested) return false;

                var angle = startAngle + angleStep * i;
                var x = centerX + radius * Math.Cos(angle);
                var y = centerY + radius * Math.Sin(angle);

                await LinearMoveAsync(
                    new[] { axisX, axisY },
                    new[] { x, y },
                    velocity,
                    cancellationToken);
            }

            return true;
        }

        /// <summary>
        /// 全部轴急停。
        /// </summary>
        public async Task EmergencyStopAllAsync()
        {
            var tasks = _axes.Values.Select(a => a.EmergencyStopAsync());
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// 关闭控制卡。
        /// </summary>
        public async Task CloseAsync()
        {
            await EmergencyStopAllAsync();
            _isInitialized = false;
        }

        public void Dispose()
        {
            CloseAsync().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 模拟单轴控制器 — 完整模拟一个运动轴的行为。
    /// 使用梯形速度曲线模拟加速→匀速→减速的运动过程。
    /// </summary>
    public class SimulationAxisController : IAxisController
    {
        private CancellationTokenSource? _motionCts;
        private readonly object _lock = new();

        /// <summary>轴配置</summary>
        public AxisConfig Config { get; }

        /// <summary>轴实时状态</summary>
        public AxisStatus Status { get; }

        public SimulationAxisController(AxisConfig config)
        {
            Config = config;
            Status = new AxisStatus { AxisId = config.AxisId };
        }

        /// <summary>使能伺服 — 模拟上电使能过程。</summary>
        public async Task<bool> ServoOnAsync()
        {
            await Task.Delay(50); // 模拟使能延迟
            Status.IsServoOn = true;
            Status.State = AxisState.Ready;
            Status.AlarmCode = 0;
            return true;
        }

        /// <summary>关闭伺服。</summary>
        public async Task<bool> ServoOffAsync()
        {
            await StopAsync();
            Status.IsServoOn = false;
            Status.State = AxisState.Disabled;
            return true;
        }

        /// <summary>
        /// 绝对定位运动 — 移动到目标位置。
        /// 使用梯形速度曲线：加速段→匀速段→减速段。
        /// </summary>
        public async Task<bool> MoveAbsoluteAsync(double position, double velocity,
            double acceleration, double deceleration, CancellationToken cancellationToken = default)
        {
            if (!Status.IsServoOn || Status.State == AxisState.Error) return false;

            // 软限位检查
            if (Config.SoftLimitEnabled)
            {
                if (position > Config.SoftLimitPositive || position < Config.SoftLimitNegative)
                    return false;
            }

            _motionCts?.Cancel();
            _motionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _motionCts.Token;

            Status.State = AxisState.Moving;
            Status.TargetPosition = position;

            var startPosition = Status.Position;
            var totalDistance = Math.Abs(position - startPosition);
            var direction = position > startPosition ? 1.0 : -1.0;

            if (totalDistance < 0.0001)
            {
                Status.State = AxisState.Done;
                return true;
            }

            // 梯形速度曲线计算
            velocity = Math.Min(velocity, Config.MaxVelocity);
            acceleration = Math.Min(acceleration, Config.MaxAcceleration);
            deceleration = Math.Min(deceleration, Config.MaxDeceleration);

            // 加速段距离
            var accelDistance = velocity * velocity / (2 * acceleration);
            // 减速段距离
            var decelDistance = velocity * velocity / (2 * deceleration);

            double accelTime, constTime, decelTime;

            if (accelDistance + decelDistance >= totalDistance)
            {
                // 三角形曲线（来不及达到最大速度就要开始减速）
                var peakVelocity = Math.Sqrt(2 * totalDistance * acceleration * deceleration / (acceleration + deceleration));
                accelTime = peakVelocity / acceleration;
                decelTime = peakVelocity / deceleration;
                constTime = 0;
            }
            else
            {
                // 梯形曲线
                accelTime = velocity / acceleration;
                decelTime = velocity / deceleration;
                var constDistance = totalDistance - accelDistance - decelDistance;
                constTime = constDistance / velocity;
            }

            var totalTime = accelTime + constTime + decelTime;

            // 模拟运动过程（每10ms更新一次位置）
            var startTime = DateTime.Now;
            var updateInterval = TimeSpan.FromMilliseconds(10);

            while (!token.IsCancellationRequested)
            {
                var elapsed = (DateTime.Now - startTime).TotalSeconds;

                if (elapsed >= totalTime)
                {
                    // 运动完成
                    Status.Position = position;
                    Status.Velocity = 0;
                    Status.State = AxisState.Done;
                    Status.UpdateTime = DateTime.Now;
                    return true;
                }

                // 根据时间计算当前位置和速度
                double currentVelocity;
                double distanceTraveled;

                if (elapsed < accelTime)
                {
                    // 加速段
                    currentVelocity = acceleration * elapsed;
                    distanceTraveled = 0.5 * acceleration * elapsed * elapsed;
                }
                else if (elapsed < accelTime + constTime)
                {
                    // 匀速段
                    var t = elapsed - accelTime;
                    currentVelocity = velocity;
                    distanceTraveled = accelDistance + velocity * t;
                }
                else
                {
                    // 减速段
                    var t = elapsed - accelTime - constTime;
                    currentVelocity = velocity - deceleration * t;
                    distanceTraveled = accelDistance + velocity * constTime
                                     + velocity * t - 0.5 * deceleration * t * t;
                }

                Status.Position = startPosition + direction * distanceTraveled;
                Status.Velocity = currentVelocity;
                Status.UpdateTime = DateTime.Now;

                try { await Task.Delay(updateInterval, token); }
                catch (OperationCanceledException) { break; }
            }

            // 被取消（停止运动）
            Status.Velocity = 0;
            Status.State = AxisState.Ready;
            return false;
        }

        /// <summary>
        /// 相对定位运动。
        /// </summary>
        public Task<bool> MoveRelativeAsync(double distance, double velocity,
            double acceleration, double deceleration, CancellationToken cancellationToken = default)
        {
            return MoveAbsoluteAsync(Status.Position + distance, velocity, acceleration, deceleration, cancellationToken);
        }

        /// <summary>
        /// JOG运动 — 持续运动直到调用Stop。
        /// </summary>
        public async Task<bool> JogAsync(double velocity, bool positive)
        {
            if (!Status.IsServoOn) return false;

            _motionCts?.Cancel();
            _motionCts = new CancellationTokenSource();
            var token = _motionCts.Token;

            Status.State = AxisState.Jogging;
            var direction = positive ? 1.0 : -1.0;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    var newPosition = Status.Position + direction * velocity * 0.01;

                    // 软限位检查
                    if (Config.SoftLimitEnabled)
                    {
                        if (newPosition > Config.SoftLimitPositive || newPosition < Config.SoftLimitNegative)
                        {
                            Status.Velocity = 0;
                            Status.State = AxisState.Ready;
                            return;
                        }
                    }

                    Status.Position = newPosition;
                    Status.Velocity = velocity;
                    Status.UpdateTime = DateTime.Now;

                    try { await Task.Delay(10, token); }
                    catch { break; }
                }

                Status.Velocity = 0;
                Status.State = AxisState.Ready;
            }, token);

            return await Task.FromResult(true);
        }

        /// <summary>
        /// 回原点 — 模拟Homing流程。
        /// </summary>
        public async Task<bool> HomeAsync(CancellationToken cancellationToken = default)
        {
            if (!Status.IsServoOn) return false;

            Status.State = AxisState.Homing;

            // 模拟回原点：先以设定速度移动到0位置
            var success = await MoveAbsoluteAsync(Config.HomeOffset, Config.HomeVelocity,
                Config.MaxAcceleration, Config.MaxDeceleration, cancellationToken);

            if (success)
            {
                Status.IsHomed = true;
                Status.State = AxisState.Done;
            }

            return success;
        }

        /// <summary>停止运动。</summary>
        public async Task StopAsync()
        {
            _motionCts?.Cancel();
            Status.Velocity = 0;
            if (Status.State == AxisState.Moving || Status.State == AxisState.Jogging)
                Status.State = AxisState.Ready;
            await Task.CompletedTask;
        }

        /// <summary>急停。</summary>
        public async Task EmergencyStopAsync()
        {
            _motionCts?.Cancel();
            Status.Velocity = 0;
            Status.State = AxisState.Ready;
            await Task.CompletedTask;
        }

        /// <summary>清除报警。</summary>
        public async Task ClearAlarmAsync()
        {
            Status.AlarmCode = 0;
            Status.State = Status.IsServoOn ? AxisState.Ready : AxisState.NotReady;
            await Task.CompletedTask;
        }

        /// <summary>等待运动完成。</summary>
        public async Task<bool> WaitForDoneAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.Now;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Status.State == AxisState.Done || Status.State == AxisState.Ready)
                    return true;

                if (DateTime.Now - startTime > timeout) return false;

                await Task.Delay(10, cancellationToken);
            }
            return false;
        }

        /// <summary>刷新状态（模拟卡不需要额外刷新）。</summary>
        public Task RefreshStatusAsync()
        {
            Status.UpdateTime = DateTime.Now;
            return Task.CompletedTask;
        }
    }
}
