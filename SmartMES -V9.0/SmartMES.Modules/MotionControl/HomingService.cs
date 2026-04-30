// ============================================================
// 文件：HomingService.cs
// 用途：多策略回零服务 — 支持传感器/编码器脉冲/硬限位/当前位置
// 设计思路：
//   工业运动控制中，回零（Home）是开机后必须执行的动作，
//   用于建立机械坐标系的绝对参考点。不同机械结构需要不同
//   的回零策略：
//   1. SensorBased  — 快速搜索原点传感器，慢速精确定位
//   2. IndexPulse   — 传感器+编码器Z脉冲二次精定位
//   3. HardStop     — 慢速运动直到堵转（力矩检测）
//   4. CurrentPosition — 直接将当前位置设为零点
//
//   回零过程：
//   ┌──────────────┐     ┌─────────────────┐     ┌──────────┐
//   │ 快速正向搜索  │ --> │ 反向慢速精确定位 │ --> │ 设零+偏移 │
//   └──────────────┘     └─────────────────┘     └──────────┘
//
//   本模块为模拟实现，模拟传感器信号和回零动作。
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 回零结果 — 记录回零过程的结果信息。
    /// </summary>
    public class HomingResult
    {
        /// <summary>是否成功。</summary>
        public bool Success { get; set; }

        /// <summary>使用的回零策略。</summary>
        public HomingStrategy Strategy { get; set; }

        /// <summary>回零后的位置（mm）。</summary>
        public double FinalPosition { get; set; }

        /// <summary>回零耗时（毫秒）。</summary>
        public double ElapsedMs { get; set; }

        /// <summary>消息/错误信息。</summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 多策略回零服务 — 根据配置自动选择回零策略并执行。
    ///
    /// 使用示例：
    ///   var svc = new HomingService();
    ///   var config = new HomingConfig { Strategy = HomingStrategy.SensorBased };
    ///   var axis = new AxisController("X");
    ///   var result = await svc.ExecuteHomingAsync(axis, config);
    /// </summary>
    public class HomingService
    {
        // 模拟传感器触发位置（mm）— 不同轴的原点传感器位置
        private readonly Dictionary<string, double> _sensorPositions = new();
        private readonly object _lock = new();
        private readonly Random _random = new(42);

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>
        /// 设置轴的模拟传感器位置。
        /// 真实系统中传感器位置由硬件决定，这里用于模拟。
        /// </summary>
        /// <param name="axisName">轴名称。</param>
        /// <param name="sensorPosition">传感器触发位置（mm）。</param>
        public void SetSensorPosition(string axisName, double sensorPosition)
        {
            lock (_lock)
            {
                _sensorPositions[axisName] = sensorPosition;
            }
        }

        /// <summary>
        /// 执行回零 — 根据配置选择策略并模拟回零过程。
        /// </summary>
        /// <param name="axis">目标轴控制器。</param>
        /// <param name="config">回零配置。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>回零结果。</returns>
        public async Task<HomingResult> ExecuteHomingAsync(
            AxisController axis, HomingConfig config, CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log($"[回零] 轴 {axis.AxisName} 开始回零，策略：{config.Strategy}");

            try
            {
                HomingResult result = config.Strategy switch
                {
                    HomingStrategy.SensorBased => await HomeSensorBasedAsync(axis, config, ct),
                    HomingStrategy.IndexPulse => await HomeIndexPulseAsync(axis, config, ct),
                    HomingStrategy.HardStop => await HomeHardStopAsync(axis, config, ct),
                    HomingStrategy.CurrentPosition => HomeCurrentPosition(axis, config),
                    _ => new HomingResult { Success = false, Message = "未知的回零策略" }
                };

                sw.Stop();
                result.ElapsedMs = sw.Elapsed.TotalMilliseconds;

                if (result.Success)
                    Log($"[回零] 轴 {axis.AxisName} 回零成功，" +
                        $"最终位置：{result.FinalPosition:F3}mm，耗时：{result.ElapsedMs:F0}ms");
                else
                    Log($"[回零] 轴 {axis.AxisName} 回零失败：{result.Message}");

                return result;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new HomingResult
                {
                    Success = false,
                    Strategy = config.Strategy,
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                    Message = "回零被取消"
                };
            }
        }

        /// <summary>
        /// 传感器回零 — 快速搜索 + 慢速精定位。
        /// 过程：
        ///   1. 以 SearchVelocity 向负方向运动，直到触发原点传感器
        ///   2. 反向以 CreepVelocity 慢速运动，离开传感器
        ///   3. 再次慢速正向运动，精确找到传感器边沿
        ///   4. 设定当前位置为零 + 偏移量
        /// </summary>
        private async Task<HomingResult> HomeSensorBasedAsync(
            AxisController axis, HomingConfig config, CancellationToken ct)
        {
            // 获取模拟传感器位置，默认为 -5mm
            double sensorPos;
            lock (_lock)
            {
                sensorPos = _sensorPositions.GetValueOrDefault(axis.AxisName, -5.0);
            }

            // 阶段1：快速搜索 — 模拟轴向负方向运动
            Log($"[回零] 阶段1：快速搜索，速度 {config.SearchVelocity} mm/s");
            double currentPos = axis.Position;
            double searchDistance = Math.Abs(currentPos - sensorPos) + 10.0;

            // 模拟搜索时间
            double searchTime = searchDistance / config.SearchVelocity;
            int searchMs = Math.Min((int)(searchTime * 1000), config.TimeoutMs / 2);
            await Task.Delay(Math.Max(searchMs, 50), ct);

            // 阶段2：慢速精定位 — 模拟反向慢速离开传感器
            Log($"[回零] 阶段2：慢速精定位，速度 {config.CreepVelocity} mm/s");
            double creepDistance = 2.0; // 慢速移动 2mm
            double creepTime = creepDistance / config.CreepVelocity;
            int creepMs = Math.Min((int)(creepTime * 1000), config.TimeoutMs / 4);
            await Task.Delay(Math.Max(creepMs, 30), ct);

            // 阶段3：设定零点 + 偏移
            double finalPos = config.Offset;
            Log($"[回零] 阶段3：设定零点，偏移量 {config.Offset}mm");

            return new HomingResult
            {
                Success = true,
                Strategy = HomingStrategy.SensorBased,
                FinalPosition = finalPos,
                Message = "传感器回零成功"
            };
        }

        /// <summary>
        /// 编码器脉冲回零 — 传感器 + Z脉冲二次精定位。
        /// 过程：
        ///   1. 先执行传感器回零流程
        ///   2. 在传感器边沿附近搜索编码器 Z 脉冲（每转一个）
        ///   3. Z 脉冲位置作为最终零点参考
        /// 精度比传感器回零更高（编码器分辨率级别）。
        /// </summary>
        private async Task<HomingResult> HomeIndexPulseAsync(
            AxisController axis, HomingConfig config, CancellationToken ct)
        {
            // 先执行传感器回零
            var sensorResult = await HomeSensorBasedAsync(axis, config, ct);
            if (!sensorResult.Success)
                return sensorResult;

            // 阶段4：搜索编码器 Z 脉冲
            Log($"[回零] 阶段4：搜索编码器 Z 脉冲");
            double zPulseOffset = _random.NextDouble() * 0.01; // 模拟 Z 脉冲微小偏移
            await Task.Delay(50, ct);

            return new HomingResult
            {
                Success = true,
                Strategy = HomingStrategy.IndexPulse,
                FinalPosition = config.Offset + zPulseOffset,
                Message = "编码器脉冲回零成功"
            };
        }

        /// <summary>
        /// 硬限位回零 — 慢速运动直到堵转。
        /// 过程：
        ///   1. 以 CreepVelocity 向负方向慢速运动
        ///   2. 检测到力矩突增（堵转信号）时停止
        ///   3. 反向移动少量距离脱离限位
        ///   4. 设定当前位置为零 + 偏移
        /// 注意：硬限位回零可能损伤机械，仅用于无传感器的场景。
        /// </summary>
        private async Task<HomingResult> HomeHardStopAsync(
            AxisController axis, HomingConfig config, CancellationToken ct)
        {
            // 阶段1：慢速向负方向运动直到堵转
            Log($"[回零] 慢速搜索硬限位，速度 {config.CreepVelocity} mm/s");
            double hardStopDist = 20.0; // 模拟20mm后堵转
            double moveTime = hardStopDist / config.CreepVelocity;
            int moveMs = Math.Min((int)(moveTime * 1000), config.TimeoutMs);
            await Task.Delay(Math.Max(moveMs, 100), ct);

            Log("[回零] 检测到堵转信号");

            // 阶段2：反向脱离硬限位
            double releaseDistance = 1.0; // 反向移动 1mm
            double releaseTime = releaseDistance / config.CreepVelocity;
            await Task.Delay(Math.Max((int)(releaseTime * 1000), 30), ct);

            return new HomingResult
            {
                Success = true,
                Strategy = HomingStrategy.HardStop,
                FinalPosition = config.Offset,
                Message = "硬限位回零成功"
            };
        }

        /// <summary>
        /// 当前位置回零 — 直接将当前位置设为零点。
        /// 最简单的回零方式，无需运动，立即完成。
        /// 适用于手动对准后设定零点的场景。
        /// </summary>
        private HomingResult HomeCurrentPosition(AxisController axis, HomingConfig config)
        {
            Log($"[回零] 当前位置设为零点，偏移量 {config.Offset}mm");

            return new HomingResult
            {
                Success = true,
                Strategy = HomingStrategy.CurrentPosition,
                FinalPosition = config.Offset,
                Message = "当前位置回零成功"
            };
        }

        /// <summary>
        /// 批量回零 — 按顺序对多个轴执行回零。
        /// </summary>
        /// <param name="axes">轴控制器列表。</param>
        /// <param name="configs">每个轴的回零配置（按轴名索引）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>每个轴的回零结果。</returns>
        public async Task<Dictionary<string, HomingResult>> HomeMultipleAsync(
            IEnumerable<AxisController> axes,
            Dictionary<string, HomingConfig>? configs = null,
            CancellationToken ct = default)
        {
            var results = new Dictionary<string, HomingResult>();
            var defaultConfig = new HomingConfig();

            foreach (var axis in axes)
            {
                var config = configs?.GetValueOrDefault(axis.AxisName) ?? defaultConfig;
                var result = await ExecuteHomingAsync(axis, config, ct);
                results[axis.AxisName] = result;

                if (!result.Success)
                {
                    Log($"[回零] 轴 {axis.AxisName} 回零失败，中断批量回零");
                    break;
                }
            }

            return results;
        }

        /// <summary>
        /// 并行回零 — 多轴同时执行回零（需要各轴互不干涉）。
        /// </summary>
        public async Task<Dictionary<string, HomingResult>> HomeParallelAsync(
            IEnumerable<AxisController> axes,
            Dictionary<string, HomingConfig>? configs = null,
            CancellationToken ct = default)
        {
            var results = new Dictionary<string, HomingResult>();
            var defaultConfig = new HomingConfig();
            var tasks = new List<(string Name, Task<HomingResult> Task)>();

            foreach (var axis in axes)
            {
                var config = configs?.GetValueOrDefault(axis.AxisName) ?? defaultConfig;
                tasks.Add((axis.AxisName, ExecuteHomingAsync(axis, config, ct)));
            }

            await Task.WhenAll(tasks.Select(t => t.Task));

            foreach (var (name, task) in tasks)
            {
                results[name] = await task;
            }

            return results;
        }

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);
    }
}
