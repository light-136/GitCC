// ============================================================
// 文件：TenAxisControllerV2.cs
// 用途：V2 十轴控制器 — 集成所有高级运动控制模块
// 设计思路：
//   TenAxisControllerV2 在 TenAxisController 基础上，集成：
//   - S曲线运动规划（可切换梯形/S曲线）
//   - 圆弧插补（G2/G3）
//   - 前瞻速度规划
//   - 电子齿轮/凸轮
//   - 坐标系管理（G54~G59）
//   - 多策略回零
//   - V2 G代码解析与执行
//
//   架构：
//   ┌─────────────────────────────────────────┐
//   │          TenAxisControllerV2            │
//   │  ┌─────────┐ ┌──────────┐ ┌────────┐  │
//   │  │ GCode   │ │ Circular │ │ LookAh │  │
//   │  │ ParserV2│ │ Interp.  │ │  ead   │  │
//   │  └────┬────┘ └────┬─────┘ └───┬────┘  │
//   │       │            │           │        │
//   │  ┌────┴────────────┴───────────┴────┐  │
//   │  │       MultiAxisController        │  │
//   │  │  ┌────┐ ┌────┐ ┌────┐   ┌────┐  │  │
//   │  │  │ X  │ │ Y  │ │ Z  │...│ S  │  │  │
//   │  │  └────┘ └────┘ └────┘   └────┘  │  │
//   │  └──────────────────────────────────┘  │
//   │  ┌──────────┐ ┌───────────┐ ┌──────┐  │
//   │  │ Coord    │ │ Homing    │ │ E-   │  │
//   │  │ Manager  │ │ Service   │ │ Gear │  │
//   │  └──────────┘ └───────────┘ └──────┘  │
//   └─────────────────────────────────────────┘
// ============================================================

using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// V2 G代码执行结果 — 扩展了 V1 的结果信息。
    /// </summary>
    public class MotionProgramResultV2
    {
        /// <summary>是否成功。</summary>
        public bool Success { get; set; }

        /// <summary>总行数。</summary>
        public int TotalLines { get; set; }

        /// <summary>已执行行数。</summary>
        public int ExecutedLines { get; set; }

        /// <summary>消息。</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>总耗时。</summary>
        public TimeSpan ElapsedTime { get; set; }

        /// <summary>圆弧插补段数。</summary>
        public int ArcSegments { get; set; }

        /// <summary>坐标系切换次数。</summary>
        public int CoordinateSystemChanges { get; set; }
    }

    /// <summary>
    /// V2 十轴控制器 — 组合所有高级运动控制模块的统一入口。
    ///
    /// 使用示例：
    ///   var ctrl = new TenAxisControllerV2();
    ///   await ctrl.HomeAllAsync();
    ///   ctrl.CoordinateManager.SetWorkOffset(CoordinateSystem.Work1,
    ///       new CoordinateTransform { OffsetX = 100, OffsetY = 50 });
    ///   var result = await ctrl.RunGCodeV2Async(GCodeParserV2.GetSampleProgramV2());
    /// </summary>
    public class TenAxisControllerV2
    {
        // ========== 子模块 ==========

        /// <summary>多轴控制器（底层轴管理）。</summary>
        private readonly MultiAxisController _mc = new();

        /// <summary>圆弧插补器。</summary>
        public CircularInterpolator CircularInterpolator { get; } = new();

        // LookAheadPlanner 是静态类，通过静态方法调用即可

        /// <summary>坐标系管理器。</summary>
        public CoordinateManager CoordinateManager { get; } = new();

        /// <summary>回零服务。</summary>
        public HomingService HomingService { get; } = new();

        /// <summary>G代码解析器V2。</summary>
        public GCodeParserV2 Parser { get; } = new();

        /// <summary>电子齿轮。</summary>
        public ElectronicGearing ElectronicGearing { get; } = new();

        // 10轴默认配置：名称、速度(mm/s)、加速度(mm/s²)
        private static readonly (string Name, double Vel, double Acc)[] AxisConfigs =
        {
            ("X", 500, 2000), ("Y", 500, 2000), ("Z", 300, 1500),
            ("A", 180, 900),  ("B", 180, 900),  ("C", 360, 1800),
            ("U", 200, 1000), ("V", 200, 1000), ("W", 150, 750),
            ("S", 100, 500),
        };

        /// <summary>所有轴的只读访问。</summary>
        public IReadOnlyDictionary<string, AxisController> Axes => _mc.Axes;

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>程序是否正在运行。</summary>
        public bool ProgramRunning { get; private set; }

        /// <summary>当前运动规划类型。</summary>
        public MotionProfileType ProfileType { get; set; } = MotionProfileType.SCurve;

        // 当前轴位置缓存（用于增量模式计算）
        private readonly Dictionary<string, double> _currentPositions = new();

        /// <summary>
        /// 构造函数 — 初始化 10 轴和所有子模块。
        /// </summary>
        public TenAxisControllerV2()
        {
            // 初始化各轴
            foreach (var (name, vel, acc) in AxisConfigs)
            {
                _mc.AddAxis(name, vel, acc);
                _currentPositions[name] = 0.0;
            }

            // 传递日志事件
            _mc.MessageLogged += (_, msg) => Log(msg);
            HomingService.MessageLogged += (_, msg) => Log(msg);
            ElectronicGearing.MessageLogged += (_, msg) => Log(msg);
        }

        // ========== 回零 ==========

        /// <summary>
        /// 全轴回零 — 使用 HomingService 按轴顺序回零。
        /// </summary>
        public async Task HomeAllAsync(
            Dictionary<string, HomingConfig>? configs = null,
            CancellationToken ct = default)
        {
            Log("[V2] 开始全轴回零");
            var results = await HomingService.HomeMultipleAsync(
                _mc.Axes.Values, configs, ct);

            foreach (var (name, result) in results)
            {
                if (result.Success)
                    _currentPositions[name] = result.FinalPosition;
            }

            Log("[V2] 全轴回零完成");
        }

        /// <summary>
        /// 全轴并行回零。
        /// </summary>
        public async Task HomeAllParallelAsync(
            Dictionary<string, HomingConfig>? configs = null,
            CancellationToken ct = default)
        {
            Log("[V2] 开始全轴并行回零");
            var results = await HomingService.HomeParallelAsync(
                _mc.Axes.Values, configs, ct);

            foreach (var (name, result) in results)
            {
                if (result.Success)
                    _currentPositions[name] = result.FinalPosition;
            }

            Log("[V2] 全轴并行回零完成");
        }

        // ========== G代码执行 ==========

        /// <summary>
        /// 执行 V2 G 代码程序 — 支持圆弧、坐标系、增量模式。
        /// </summary>
        /// <param name="program">G 代码程序文本。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>执行结果。</returns>
        public async Task<MotionProgramResultV2> RunGCodeV2Async(
            string program, CancellationToken ct = default)
        {
            ProgramRunning = true;
            var start = DateTime.Now;
            int executed = 0;
            int arcSegments = 0;
            int csChanges = 0;

            try
            {
                var commands = Parser.Parse(program);
                Log($"[V2] 解析完成，共 {commands.Count} 条指令");

                foreach (var cmd in commands)
                {
                    if (ct.IsCancellationRequested) break;

                    Log($"[V2-EXEC] 行{cmd.LineNumber}: {cmd.Raw}");

                    switch (cmd.Type)
                    {
                        case GCodeTypeV2.G0:
                        case GCodeTypeV2.G1:
                            await ExecuteLinearMoveAsync(cmd, ct);
                            break;

                        case GCodeTypeV2.G2:
                        case GCodeTypeV2.G3:
                            arcSegments += await ExecuteArcMoveAsync(cmd, ct);
                            break;

                        case GCodeTypeV2.G4:
                            await ExecuteDwellAsync(cmd, ct);
                            break;

                        case GCodeTypeV2.G17:
                        case GCodeTypeV2.G18:
                        case GCodeTypeV2.G19:
                            Log($"[V2] 平面选择：{cmd.Type}");
                            break;

                        case GCodeTypeV2.G28:
                            await HomeAllAsync(ct: ct);
                            ResetCurrentPositions();
                            break;

                        case GCodeTypeV2.G54:
                        case GCodeTypeV2.G55:
                        case GCodeTypeV2.G56:
                        case GCodeTypeV2.G57:
                        case GCodeTypeV2.G58:
                        case GCodeTypeV2.G59:
                            csChanges++;
                            Log($"[V2] 坐标系切换：{cmd.Type} → {cmd.CoordinateSystem}");
                            break;

                        case GCodeTypeV2.G90:
                            Log("[V2] 切换到绝对坐标模式");
                            break;

                        case GCodeTypeV2.G91:
                            Log("[V2] 切换到增量坐标模式");
                            break;

                        case GCodeTypeV2.M0:
                            Log("[V2] 程序暂停");
                            await Task.Delay(1000, ct);
                            break;

                        case GCodeTypeV2.M2:
                        case GCodeTypeV2.M30:
                            Log($"[V2] 程序结束 ({cmd.Type})");
                            executed++;
                            goto done;
                    }

                    executed++;
                }

            done:
                return new MotionProgramResultV2
                {
                    Success = true,
                    TotalLines = commands.Count,
                    ExecutedLines = executed,
                    ElapsedTime = DateTime.Now - start,
                    ArcSegments = arcSegments,
                    CoordinateSystemChanges = csChanges,
                    Message = $"V2 程序完成：{executed}/{commands.Count} 行，" +
                              $"圆弧段数：{arcSegments}，坐标系切换：{csChanges} 次"
                };
            }
            catch (OperationCanceledException)
            {
                return new MotionProgramResultV2
                {
                    Success = false,
                    TotalLines = 0,
                    ExecutedLines = executed,
                    ElapsedTime = DateTime.Now - start,
                    Message = "程序被取消"
                };
            }
            finally
            {
                ProgramRunning = false;
            }
        }

        // ========== 指令执行 ==========

        /// <summary>
        /// 执行直线运动（G0/G1）— 支持绝对/增量模式和坐标系变换。
        /// </summary>
        private async Task ExecuteLinearMoveAsync(GCodeCommandV2 cmd, CancellationToken ct)
        {
            foreach (var (axisName, rawTarget) in cmd.AxisPositions)
            {
                if (!_mc.Axes.TryGetValue(axisName, out var axis)) continue;

                // 计算目标位置
                double target = cmd.IsAbsolute ? rawTarget : _currentPositions[axisName] + rawTarget;

                // 坐标系变换（仅对 X/Y/Z 应用）
                if (cmd.CoordinateSystem != CoordinateSystem.Machine &&
                    (axisName == "X" || axisName == "Y" || axisName == "Z"))
                {
                    var workPos = (
                        X: axisName == "X" ? target : _currentPositions["X"],
                        Y: axisName == "Y" ? target : _currentPositions["Y"],
                        Z: axisName == "Z" ? target : _currentPositions["Z"]
                    );
                    var machinePos = CoordinateManager.WorkToMachine(
                        workPos.X, workPos.Y, workPos.Z, cmd.CoordinateSystem);

                    target = axisName switch
                    {
                        "X" => machinePos.X,
                        "Y" => machinePos.Y,
                        "Z" => machinePos.Z,
                        _ => target
                    };
                }

                // 设置速度
                if (cmd.Type == GCodeTypeV2.G0)
                    axis.Velocity = axis.Velocity; // 使用轴默认最大速度
                else
                    axis.Velocity = Math.Min(cmd.FeedRate / 60.0, axis.Velocity);

                axis.MoveTo(target);
                _currentPositions[axisName] = cmd.IsAbsolute ? rawTarget : _currentPositions[axisName] + rawTarget;
            }

            // 等待所有运动轴完成
            await WaitForAxesAsync(cmd.AxisPositions.Keys, ct);
        }

        /// <summary>
        /// 执行圆弧运动（G2/G3）— 分解为微线段后逐段执行。
        /// </summary>
        /// <returns>生成的微线段数量。</returns>
        private async Task<int> ExecuteArcMoveAsync(GCodeCommandV2 cmd, CancellationToken ct)
        {
            // 获取起终点坐标
            double startX = _currentPositions.GetValueOrDefault("X", 0);
            double startY = _currentPositions.GetValueOrDefault("Y", 0);

            double endX = cmd.IsAbsolute
                ? cmd.AxisPositions.GetValueOrDefault("X", startX)
                : startX + cmd.AxisPositions.GetValueOrDefault("X", 0);

            double endY = cmd.IsAbsolute
                ? cmd.AxisPositions.GetValueOrDefault("Y", startY)
                : startY + cmd.AxisPositions.GetValueOrDefault("Y", 0);

            bool isClockwise = cmd.Type == GCodeTypeV2.G2;

            // 构建圆弧参数
            var arcParams = new ArcParameters { IsClockwise = isClockwise };

            if (cmd.R.HasValue)
            {
                arcParams.UseRadiusMode = true;
                arcParams.Radius = cmd.R.Value;
            }
            else
            {
                arcParams.UseRadiusMode = false;
                arcParams.CenterI = cmd.I ?? 0;
                arcParams.CenterJ = cmd.J ?? 0;
            }

            // 生成微线段点序列
            var points = CircularInterpolator.GenerateArcPoints(
                startX, startY, endX, endY, arcParams, cmd.FeedRate);

            Log($"[V2-ARC] {cmd.Type} 圆弧分解为 {points.Count} 个微线段");

            // 逐段执行微线段
            foreach (var pt in points)
            {
                if (ct.IsCancellationRequested) break;

                if (pt.AxisTargets.TryGetValue("X", out double ptX) && _mc.Axes.TryGetValue("X", out var xAxis))
                {
                    xAxis.Velocity = cmd.FeedRate / 60.0;
                    xAxis.MoveTo(ptX);
                }
                if (pt.AxisTargets.TryGetValue("Y", out double ptY) && _mc.Axes.TryGetValue("Y", out var yAxis))
                {
                    yAxis.Velocity = cmd.FeedRate / 60.0;
                    yAxis.MoveTo(ptY);
                }

                await WaitForAxesAsync(new[] { "X", "Y" }, ct);
            }

            // 更新当前位置
            _currentPositions["X"] = endX;
            _currentPositions["Y"] = endY;

            return points.Count;
        }

        /// <summary>
        /// 执行暂停（G4）。
        /// </summary>
        private async Task ExecuteDwellAsync(GCodeCommandV2 cmd, CancellationToken ct)
        {
            double ms = 0;

            if (cmd.P.HasValue)
                ms = cmd.P.Value;
            else if (cmd.DwellSeconds.HasValue)
                ms = cmd.DwellSeconds.Value * 1000;

            if (ms > 0)
            {
                Log($"[V2] 暂停 {ms:F0} 毫秒");
                await Task.Delay((int)Math.Min(ms, 30000), ct);
            }
        }

        // ========== 辅助方法 ==========

        /// <summary>
        /// 等待指定轴的运动完成。
        /// </summary>
        private async Task WaitForAxesAsync(IEnumerable<string> axisNames, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                var axes = axisNames
                    .Where(n => _mc.Axes.ContainsKey(n))
                    .Select(n => _mc.Axes[n])
                    .ToList();

                while (axes.Any(a => a.State == AxisState.Running) && !ct.IsCancellationRequested)
                    Thread.Sleep(10);
            }, ct);
        }

        /// <summary>
        /// 重置所有轴的当前位置记录为 0。
        /// </summary>
        private void ResetCurrentPositions()
        {
            foreach (var key in _currentPositions.Keys.ToList())
                _currentPositions[key] = 0.0;
        }

        /// <summary>
        /// 紧急停止 — 停止所有运动。
        /// </summary>
        public void EmergencyStop()
        {
            _mc.EmergencyStop();
            Log("[V2] 紧急停止");
        }

        /// <summary>
        /// 复位所有轴。
        /// </summary>
        public void ResetAll()
        {
            _mc.ResetAll();
            Log("[V2] 所有轴已复位");
        }

        /// <summary>
        /// 获取所有轴的当前位置。
        /// </summary>
        public Dictionary<string, double> GetAllPositions()
        {
            return _mc.Axes.ToDictionary(kv => kv.Key, kv => kv.Value.Position);
        }

        /// <summary>
        /// 获取 V2 样例 G 代码程序。
        /// </summary>
        public static string GetSampleProgram() => GCodeParserV2.GetSampleProgramV2();

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);
    }
}
