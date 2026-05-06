// ============================================================
// 文件：TenAxisControllerV2.cs
// 用途：集成运动控制器V2 — 组合所有高级运动控制模块
// 设计思路：
//   TenAxisControllerV2 是运动控制系统的顶层门面（Facade），
//   将以下子模块统一编排：
//   - 多轴控制器（MultiAxisController / TenAxisController）
//   - S曲线/梯形运动规划
//   - 圆弧插补器
//   - 前瞻速度规划器
//   - 电子齿轮与凸轮
//   - 坐标系管理
//   - 多策略回零
//   - 扩展G代码解析器
//   用户通过此类即可使用完整的运动控制功能。
// ============================================================

using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// V2 集成控制器配置 — 可配置各子模块的参数。
    /// </summary>
    public class ControllerV2Config
    {
        /// <summary>轴名称列表。</summary>
        public List<string> AxisNames { get; set; } = new() { "X", "Y", "Z" };

        /// <summary>最大速度（mm/s）。</summary>
        public double MaxVelocity { get; set; } = 500;

        /// <summary>最大加速度（mm/s²）。</summary>
        public double MaxAcceleration { get; set; } = 2000;

        /// <summary>最大加加速度/急动度（mm/s³），用于S曲线。</summary>
        public double MaxJerk { get; set; } = 10000;

        /// <summary>运动规划类型（梯形或S曲线）。</summary>
        public MotionProfileType ProfileType { get; set; } = MotionProfileType.SCurve;

        /// <summary>前瞻拐角容差（0~1）。</summary>
        public double CornerTolerance { get; set; } = 0.05;

        /// <summary>是否启用前瞻规划。</summary>
        public bool EnableLookAhead { get; set; } = true;
    }

    /// <summary>
    /// 集成运动控制器V2 — 组合所有高级运动控制子模块的门面类。
    ///
    /// 主要功能：
    ///   1. 管理多轴及其运动状态
    ///   2. 执行G代码程序（含圆弧、坐标系、模态）
    ///   3. 前瞻速度优化
    ///   4. 电子齿轮/凸轮主从同步
    ///   5. 坐标系变换（G54~G59）
    ///   6. 多策略回零
    ///
    /// 使用方式：
    ///   var ctrl = new TenAxisControllerV2(config);
    ///   await ctrl.ExecuteGCodeProgramAsync(gcodeProgram);
    /// </summary>
    public class TenAxisControllerV2 : IDisposable
    {
        // ========== 子模块实例 ==========

        /// <summary>所有轴控制器，按名称索引。</summary>
        public Dictionary<string, AxisController> Axes { get; } = new();

        /// <summary>坐标系管理器。</summary>
        public CoordinateManager CoordinateManager { get; }

        /// <summary>G代码解析器V2。</summary>
        public GCodeParserV2 GCodeParser { get; }

        /// <summary>圆弧插补器。</summary>
        public CircularInterpolator ArcInterpolator { get; }

        /// <summary>电子齿轮。</summary>
        public ElectronicGearing Gearing { get; }

        /// <summary>电子凸轮。</summary>
        public ElectronicCamming Camming { get; }

        /// <summary>各轴回零服务。</summary>
        public Dictionary<string, HomingService> HomingServices { get; } = new();

        /// <summary>控制器配置。</summary>
        public ControllerV2Config Config { get; }

        // 运行状态
        private CancellationTokenSource? _programCts;
        private bool _disposed;

        /// <summary>是否正在执行程序。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>程序执行进度（0~100）。</summary>
        public double Progress { get; private set; }

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>进度更新事件。</summary>
        public event EventHandler<double>? ProgressChanged;

        /// <summary>程序完成事件。</summary>
        public event EventHandler<bool>? ProgramCompleted;

        /// <summary>
        /// 构造函数 — 初始化所有子模块。
        /// </summary>
        /// <param name="config">控制器配置。</param>
        public TenAxisControllerV2(ControllerV2Config? config = null)
        {
            Config = config ?? new ControllerV2Config();

            // 初始化坐标系管理器
            CoordinateManager = new CoordinateManager();

            // 初始化各轴
            foreach (var name in Config.AxisNames)
            {
                var axis = new AxisController
                {
                    AxisName = name,
                    Velocity = Config.MaxVelocity,
                    Acceleration = Config.MaxAcceleration
                };
                Axes[name] = axis;

                // 为每个轴创建回零服务
                HomingServices[name] = new HomingService(axis);
            }

            // 初始化圆弧插补器
            ArcInterpolator = new CircularInterpolator();

            // 初始化G代码解析器（传入坐标系管理器）
            GCodeParser = new GCodeParserV2(CoordinateManager);

            // 初始化电子齿轮和凸轮
            Gearing = new ElectronicGearing();
            Camming = new ElectronicCamming();

            // 转发子模块日志
            GCodeParser.MessageLogged += (_, msg) => Log(msg);
            Gearing.MessageLogged += (_, msg) => Log(msg);
            Camming.MessageLogged += (_, msg) => Log(msg);
            foreach (var hs in HomingServices.Values)
            {
                hs.MessageLogged += (_, msg) => Log(msg);
            }
        }

        // ========== G代码程序执行 ==========

        /// <summary>
        /// 执行G代码程序 — 解析、前瞻规划、逐段执行。
        ///
        /// 执行流程：
        ///   1. 解析G代码文本，生成插补点序列
        ///   2. 如果启用前瞻，对路径执行三遍速度规划
        ///   3. 逐段移动各轴到目标位置
        ///   4. 等待每段运动完成
        /// </summary>
        /// <param name="program">G代码程序文本。</param>
        /// <param name="ct">外部取消令牌。</param>
        public async Task ExecuteGCodeProgramAsync(string program, CancellationToken ct = default)
        {
            if (IsRunning)
                throw new InvalidOperationException("程序已在运行中");

            _programCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            IsRunning = true;
            Progress = 0;

            try
            {
                Log("[控制器V2] 开始解析G代码程序");

                // 步骤1：解析G代码，生成插补点序列
                var path = GCodeParser.ParseProgram(program);
                Log($"[控制器V2] 解析完成，共 {path.Count} 个插补点");

                if (path.Count == 0)
                {
                    Log("[控制器V2] 程序为空，无需执行");
                    return;
                }

                // 步骤2：前瞻速度规划（可选）
                List<PlannedSegment>? planned = null;
                if (Config.EnableLookAhead && path.Count >= 2)
                {
                    planned = LookAheadPlanner.Plan(
                        path, Config.MaxVelocity, Config.MaxAcceleration, Config.CornerTolerance);
                    Log($"[控制器V2] 前瞻规划完成，{planned.Count} 个规划段");
                }

                // 步骤3：逐段执行
                int totalSteps = planned?.Count ?? path.Count;
                for (int i = 0; i < totalSteps; i++)
                {
                    _programCts.Token.ThrowIfCancellationRequested();

                    InterpolationPoint target;
                    double velocity;

                    if (planned != null)
                    {
                        // 使用前瞻规划的速度
                        var seg = planned[i];
                        target = seg.Target;
                        velocity = Math.Max(seg.EntryVelocity,
                                           seg.ExitVelocity);
                        if (velocity < 1) velocity = Config.MaxVelocity;
                    }
                    else
                    {
                        target = path[i];
                        velocity = target.FeedRate > 0 ? target.FeedRate : Config.MaxVelocity;
                    }

                    // 移动各轴到目标位置
                    await MoveAxesToTargetAsync(target, velocity, _programCts.Token);

                    // 更新进度
                    Progress = (double)(i + 1) / totalSteps * 100;
                    ProgressChanged?.Invoke(this, Progress);
                }

                Log("[控制器V2] 程序执行完成");
                ProgramCompleted?.Invoke(this, true);
            }
            catch (OperationCanceledException)
            {
                Log("[控制器V2] 程序执行被取消");
                StopAllAxes();
                ProgramCompleted?.Invoke(this, false);
            }
            catch (Exception ex)
            {
                Log($"[控制器V2] 程序执行异常：{ex.Message}");
                StopAllAxes();
                ProgramCompleted?.Invoke(this, false);
                throw;
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// 停止当前正在执行的程序。
        /// </summary>
        public void StopProgram()
        {
            _programCts?.Cancel();
            StopAllAxes();
            Log("[控制器V2] 程序已停止");
        }

        // ========== 单轴与多轴操作 ==========

        /// <summary>
        /// 移动指定轴到目标位置（绝对坐标）。
        /// </summary>
        public async Task MoveAxisAsync(string axisName, double target,
                                         double velocity, CancellationToken ct = default)
        {
            if (!Axes.TryGetValue(axisName, out var axis))
                throw new ArgumentException($"未找到轴：{axisName}");

            axis.Velocity = velocity;
            axis.MoveTo(target);

            // 等待轴运动完成
            while (axis.State != AxisState.Idle && !ct.IsCancellationRequested)
            {
                await Task.Delay(10, ct);
            }
        }

        /// <summary>
        /// 移动多轴到插补点目标位置。
        /// </summary>
        private async Task MoveAxesToTargetAsync(InterpolationPoint target,
                                                  double velocity, CancellationToken ct)
        {
            // 启动各轴运动
            foreach (var (axisName, targetPos) in target.AxisTargets)
            {
                if (Axes.TryGetValue(axisName, out var axis))
                {
                    axis.Velocity = velocity;
                    axis.MoveTo(targetPos);
                }
            }

            // 等待所有轴到达目标
            var movingAxes = target.AxisTargets.Keys
                .Where(name => Axes.ContainsKey(name))
                .Select(name => Axes[name])
                .ToList();

            while (movingAxes.Any(a => a.State != AxisState.Idle) && !ct.IsCancellationRequested)
            {
                await Task.Delay(5, ct);
            }
        }

        /// <summary>
        /// 停止所有轴。
        /// </summary>
        public void StopAllAxes()
        {
            foreach (var axis in Axes.Values)
            {
                axis.Stop();
            }
        }

        // ========== 回零操作 ==========

        /// <summary>
        /// 对指定轴执行回零。
        /// </summary>
        public async Task<HomingResult> HomeAxisAsync(string axisName, HomingConfig config,
                                                       CancellationToken ct = default)
        {
            if (!HomingServices.TryGetValue(axisName, out var service))
                throw new ArgumentException($"未找到轴的回零服务：{axisName}");

            return await service.ExecuteHomingAsync(config, ct);
        }

        /// <summary>
        /// 对所有轴依次执行回零。
        /// </summary>
        public async Task<Dictionary<string, HomingResult>> HomeAllAxesAsync(
            HomingConfig config, CancellationToken ct = default)
        {
            var results = new Dictionary<string, HomingResult>();

            foreach (var axisName in Config.AxisNames)
            {
                Log($"[控制器V2] 开始回零：{axisName}");
                results[axisName] = await HomeAxisAsync(axisName, config, ct);
            }

            return results;
        }

        // ========== 坐标系操作 ==========

        /// <summary>
        /// 设置工件坐标系偏移。
        /// </summary>
        public void SetWorkOffset(CoordinateSystem cs, double offsetX, double offsetY,
                                   double offsetZ, double rotationDeg = 0)
        {
            CoordinateManager.SetWorkOffset(cs, new CoordinateTransform
            {
                OffsetX = offsetX,
                OffsetY = offsetY,
                OffsetZ = offsetZ,
                RotationDeg = rotationDeg
            });

            Log($"[控制器V2] 设置坐标系 {cs}：偏移=({offsetX},{offsetY},{offsetZ})，旋转={rotationDeg}°");
        }

        /// <summary>
        /// 获取指定坐标系下的当前位置。
        /// </summary>
        public (double X, double Y, double Z) GetPositionInCoordinateSystem(CoordinateSystem cs)
        {
            double mx = Axes.GetValueOrDefault("X")?.Position ?? 0;
            double my = Axes.GetValueOrDefault("Y")?.Position ?? 0;
            double mz = Axes.GetValueOrDefault("Z")?.Position ?? 0;

            return CoordinateManager.MachineToWork(mx, my, mz, cs);
        }

        // ========== 电子齿轮/凸轮操作 ==========

        /// <summary>
        /// 启用电子齿轮 — 从轴按比例跟踪主轴。
        /// </summary>
        public void EnableGearing(string masterAxis, string slaveAxis,
                                   double ratio = 1.0, double phaseOffset = 0)
        {
            if (!Axes.TryGetValue(masterAxis, out var master) ||
                !Axes.TryGetValue(slaveAxis, out var slave))
                throw new ArgumentException("主轴或从轴不存在");

            Gearing.Enable(master, slave, ratio, phaseOffset);
        }

        /// <summary>
        /// 启用电子凸轮 — 从轴按凸轮表跟踪主轴。
        /// </summary>
        public void EnableCamming(string masterAxis, string slaveAxis, CamProfile profile)
        {
            if (!Axes.TryGetValue(masterAxis, out var master) ||
                !Axes.TryGetValue(slaveAxis, out var slave))
                throw new ArgumentException("主轴或从轴不存在");

            Camming.Enable(master, slave, profile);
        }

        /// <summary>
        /// 禁用电子齿轮。
        /// </summary>
        public void DisableGearing() => Gearing.Disable();

        /// <summary>
        /// 禁用电子凸轮。
        /// </summary>
        public void DisableCamming() => Camming.Disable();

        // ========== 状态查询 ==========

        /// <summary>
        /// 获取所有轴的状态摘要。
        /// </summary>
        public Dictionary<string, (double Position, AxisState State, bool IsHomed)> GetAxisStates()
        {
            return Axes.ToDictionary(
                kvp => kvp.Key,
                kvp => (kvp.Value.Position, kvp.Value.State, kvp.Value.IsHomed));
        }

        /// <summary>
        /// 检查所有轴是否已回零。
        /// </summary>
        public bool AllAxesHomed => Axes.Values.All(a => a.IsHomed);

        // ========== 辅助方法 ==========

        private void Log(string message)
        {
            MessageLogged?.Invoke(this, message);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopProgram();
            Gearing.Dispose();
            Camming.Dispose();
            _programCts?.Dispose();
        }
    }
}
