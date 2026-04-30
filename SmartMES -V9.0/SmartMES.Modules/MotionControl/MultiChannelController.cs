// ============================================================
// 文件：MultiChannelController.cs
// 用途：多通道运动控制器 — 30轴工业运动控制系统的多通道并行执行引擎
//
// 设计思路：
//   在大型工业设备（如多主轴加工中心、多工位装配线）中，
//   需要同时运行多个独立的 G 代码程序，每个程序控制一组不同的轴。
//   多通道控制器将轴按通道分组，每个通道拥有独立的 G 代码解析器
//   和执行上下文，可以并行运行各自的加工程序。
//
//   核心架构：
//   1. MotionChannel（内部类）— 封装单个通道的状态、解析器和执行进度
//   2. MultiChannelController — 管理所有通道的生命周期：
//      - 轴注册与通道分配
//      - 通道程序的启动、暂停、恢复、停止
//      - 通道间同步屏障（SyncBarrier）协调
//      - 全局状态查询
//
//   同步屏障机制：
//   当多个通道需要在某个时刻同步（例如，一个通道完成粗加工后
//   等待另一个通道也完成），可以使用 SyncBarrier。
//   每个 Barrier 包含参与同步的通道列表，
//   当所有通道都调用 WaitForSyncBarrier 后才会同时继续。
//
//   线程安全：
//   - 每个通道使用独立的 CancellationTokenSource 控制执行流
//   - 通道状态切换通过锁保护
//   - 同步屏障使用计数器 + TaskCompletionSource 实现等待
// ============================================================

using SmartMES.Core.Models;
using SmartMES.Core.Interfaces;

namespace SmartMES.Modules.MotionControl
{
    /// <summary>
    /// 运动通道 — 封装单个 G 代码执行通道的完整上下文。
    /// 每个通道管理一组轴，独立解析和执行 G 代码程序。
    /// </summary>
    public class MotionChannel
    {
        /// <summary>通道配置（编号、名称、轴列表、默认进给速度）。</summary>
        public ChannelConfig Config { get; set; }

        /// <summary>通道当前运行状态。</summary>
        public ChannelState State { get; set; } = ChannelState.Idle;

        /// <summary>G 代码解析器实例（每通道独立，维护各自的模态状态）。</summary>
        public GCodeParserV2 Parser { get; set; }

        /// <summary>取消令牌源 — 用于取消当前通道的程序执行。</summary>
        public CancellationTokenSource? _cts;

        /// <summary>当前正在执行的 G 代码程序文本。</summary>
        public string CurrentProgram { get; set; } = string.Empty;

        /// <summary>当前执行到的行号（从1开始）。</summary>
        public int CurrentLine { get; set; }

        /// <summary>程序总行数（指令数量）。</summary>
        public int TotalLines { get; set; }

        /// <summary>
        /// 同步屏障等待器 — 当通道到达同步点时，
        /// 通过 TaskCompletionSource 等待其他通道也到达。
        /// </summary>
        public TaskCompletionSource<bool>? _syncWaiter;

        /// <summary>
        /// 构造函数 — 初始化通道配置和解析器。
        /// </summary>
        /// <param name="config">通道配置信息。</param>
        public MotionChannel(ChannelConfig config)
        {
            Config = config;
            Parser = new GCodeParserV2();
        }
    }

    /// <summary>
    /// 多通道运动控制器 — 管理多个独立 G 代码执行通道的并行运行。
    ///
    /// 使用示例：
    ///   var controller = new MultiChannelController();
    ///
    ///   // 注册轴
    ///   controller.RegisterAxis(new AxisController("X1"));
    ///   controller.RegisterAxis(new AxisController("Y1"));
    ///   controller.RegisterAxis(new AxisController("X2"));
    ///   controller.RegisterAxis(new AxisController("Y2"));
    ///
    ///   // 添加通道
    ///   controller.AddChannel(new ChannelConfig { Id = 1, Name = "通道1", AxisNames = new() { "X1", "Y1" } });
    ///   controller.AddChannel(new ChannelConfig { Id = 2, Name = "通道2", AxisNames = new() { "X2", "Y2" } });
    ///
    ///   // 并行启动两个通道
    ///   var programs = new Dictionary&lt;int, string&gt;
    ///   {
    ///       { 1, "G1 X1:100 Y1:50 F1000\nM30" },
    ///       { 2, "G1 X2:200 Y2:80 F1500\nM30" }
    ///   };
    ///   await controller.StartAllChannelsAsync(programs);
    /// </summary>
    public class MultiChannelController
    {
        // ==================== 私有字段 ====================

        /// <summary>通道字典 — 键为通道编号，值为通道实例。</summary>
        private readonly Dictionary<int, MotionChannel> _channels = new();

        /// <summary>共享轴引用字典 — 键为轴名称，值为轴控制器实例。</summary>
        private readonly Dictionary<string, AxisController> _axes = new();

        /// <summary>同步屏障字典 — 键为屏障编号，值为屏障定义。</summary>
        private readonly Dictionary<int, SyncBarrier> _barriers = new();

        /// <summary>
        /// 同步屏障计数器 — 记录每个屏障已到达的通道数量。
        /// 键为屏障编号，值为已到达计数。
        /// </summary>
        private readonly Dictionary<int, int> _barrierCounters = new();

        /// <summary>
        /// 同步屏障完成信号 — 当所有通道到达屏障时触发。
        /// 键为屏障编号，值为完成信号源。
        /// </summary>
        private readonly Dictionary<int, TaskCompletionSource<bool>> _barrierSignals = new();

        /// <summary>同步屏障操作锁 — 保护屏障计数器的线程安全。</summary>
        private readonly object _barrierLock = new();

        /// <summary>通道操作锁 — 保护通道字典的线程安全。</summary>
        private readonly object _channelLock = new();

        // ==================== 事件 ====================

        /// <summary>日志消息事件 — 用于向外部报告运行状态和调试信息。</summary>
        public event EventHandler<string>? MessageLogged;

        // ==================== 轴管理 ====================

        /// <summary>
        /// 注册轴控制器 — 将轴加入共享轴池，供通道引用。
        /// 同一个轴只能注册一次，重复注册会覆盖。
        /// </summary>
        /// <param name="axis">轴控制器实例。</param>
        public void RegisterAxis(AxisController axis)
        {
            _axes[axis.AxisName] = axis;
            Log($"[多通道] 注册轴: {axis.AxisName}");
        }

        // ==================== 通道管理 ====================

        /// <summary>
        /// 添加通道 — 根据配置创建新的执行通道。
        /// 会验证配置中引用的所有轴是否已注册。
        /// </summary>
        /// <param name="config">通道配置。</param>
        /// <exception cref="ArgumentException">通道编号已存在或引用了未注册的轴。</exception>
        public void AddChannel(ChannelConfig config)
        {
            lock (_channelLock)
            {
                // 检查通道编号是否已存在
                if (_channels.ContainsKey(config.Id))
                {
                    throw new ArgumentException($"通道 {config.Id} 已存在，无法重复添加。");
                }

                // 验证所有引用的轴是否已注册
                foreach (var axisName in config.AxisNames)
                {
                    if (!_axes.ContainsKey(axisName))
                    {
                        throw new ArgumentException(
                            $"通道 {config.Id}('{config.Name}') 引用了未注册的轴: {axisName}");
                    }
                }

                // 创建通道实例并加入字典
                var channel = new MotionChannel(config);
                _channels[config.Id] = channel;
                Log($"[多通道] 添加通道: {config.Id}('{config.Name}'), 轴: [{string.Join(", ", config.AxisNames)}]");
            }
        }

        /// <summary>
        /// 移除通道 — 停止并删除指定通道。
        /// 如果通道正在运行，会先停止执行。
        /// </summary>
        /// <param name="channelId">通道编号。</param>
        public void RemoveChannel(int channelId)
        {
            lock (_channelLock)
            {
                if (!_channels.TryGetValue(channelId, out var channel))
                {
                    Log($"[多通道] 移除通道失败: 通道 {channelId} 不存在");
                    return;
                }

                // 如果通道正在运行，先停止
                if (channel.State == ChannelState.Running || channel.State == ChannelState.Paused)
                {
                    StopChannel(channelId);
                }

                _channels.Remove(channelId);
                Log($"[多通道] 已移除通道: {channelId}('{channel.Config.Name}')");
            }
        }

        /// <summary>
        /// 获取通道状态。
        /// </summary>
        /// <param name="channelId">通道编号。</param>
        /// <returns>通道当前状态。</returns>
        /// <exception cref="KeyNotFoundException">通道不存在。</exception>
        public ChannelState GetChannelState(int channelId)
        {
            lock (_channelLock)
            {
                if (!_channels.TryGetValue(channelId, out var channel))
                {
                    throw new KeyNotFoundException($"通道 {channelId} 不存在。");
                }
                return channel.State;
            }
        }

        // ==================== 通道执行 ====================

        /// <summary>
        /// 启动单个通道 — 解析 G 代码程序并在通道的轴上执行。
        /// 仅处理 G0/G1（直线运动），其他指令记录日志后跳过。
        /// </summary>
        /// <param name="channelId">通道编号。</param>
        /// <param name="gcodeProgram">G 代码程序文本。</param>
        /// <param name="ct">外部取消令牌（可选）。</param>
        /// <exception cref="KeyNotFoundException">通道不存在。</exception>
        /// <exception cref="InvalidOperationException">通道不在空闲状态。</exception>
        public async Task StartChannelAsync(int channelId, string gcodeProgram, CancellationToken ct = default)
        {
            MotionChannel channel;

            lock (_channelLock)
            {
                if (!_channels.TryGetValue(channelId, out channel!))
                {
                    throw new KeyNotFoundException($"通道 {channelId} 不存在。");
                }

                if (channel.State != ChannelState.Idle)
                {
                    throw new InvalidOperationException(
                        $"通道 {channelId} 当前状态为 {channel.State}，只能在空闲状态下启动。");
                }
            }

            // 解析 G 代码程序
            var commands = channel.Parser.Parse(gcodeProgram);
            channel.CurrentProgram = gcodeProgram;
            channel.TotalLines = commands.Count;
            channel.CurrentLine = 0;

            // 创建通道专属取消令牌源，与外部令牌关联
            channel._cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            channel.State = ChannelState.Running;

            Log($"[通道{channelId}] 启动程序执行，共 {commands.Count} 条指令");

            try
            {
                // 执行通道程序
                await ExecuteChannelProgramAsync(channel, channel._cts.Token);
                channel.State = ChannelState.Idle;
                Log($"[通道{channelId}] 程序执行完成");
            }
            catch (OperationCanceledException)
            {
                // 取消操作 — 状态已在 StopChannel 中设置
                Log($"[通道{channelId}] 程序执行被取消");
            }
            catch (Exception ex)
            {
                channel.State = ChannelState.Error;
                Log($"[通道{channelId}] 程序执行出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 启动所有通道 — 为每个通道指定 G 代码程序，并行执行。
        /// 使用 Task.WhenAll 实现真正的多通道并行。
        /// </summary>
        /// <param name="programs">通道编号到 G 代码程序的映射。</param>
        /// <param name="ct">外部取消令牌（可选）。</param>
        public async Task StartAllChannelsAsync(Dictionary<int, string> programs, CancellationToken ct = default)
        {
            Log($"[多通道] 并行启动 {programs.Count} 个通道");

            // 为每个通道创建启动任务
            var tasks = programs.Select(kvp =>
                StartChannelAsync(kvp.Key, kvp.Value, ct)
            ).ToArray();

            // 等待所有通道执行完成
            await Task.WhenAll(tasks);

            Log($"[多通道] 所有通道执行完成");
        }

        // ==================== 通道控制 ====================

        /// <summary>
        /// 暂停通道 — 暂停指定通道中所有轴的运动。
        /// </summary>
        /// <param name="channelId">通道编号。</param>
        public void PauseChannel(int channelId)
        {
            lock (_channelLock)
            {
                if (!_channels.TryGetValue(channelId, out var channel))
                {
                    Log($"[多通道] 暂停失败: 通道 {channelId} 不存在");
                    return;
                }

                if (channel.State != ChannelState.Running)
                {
                    Log($"[多通道] 暂停失败: 通道 {channelId} 当前不在运行状态");
                    return;
                }

                // 暂停通道中的所有轴
                foreach (var axisName in channel.Config.AxisNames)
                {
                    if (_axes.TryGetValue(axisName, out var axis))
                    {
                        axis.Pause();
                    }
                }

                channel.State = ChannelState.Paused;
                Log($"[通道{channelId}] 已暂停，当前行: {channel.CurrentLine}/{channel.TotalLines}");
            }
        }

        /// <summary>
        /// 恢复通道 — 恢复指定通道中所有轴的运动。
        /// </summary>
        /// <param name="channelId">通道编号。</param>
        public void ResumeChannel(int channelId)
        {
            lock (_channelLock)
            {
                if (!_channels.TryGetValue(channelId, out var channel))
                {
                    Log($"[多通道] 恢复失败: 通道 {channelId} 不存在");
                    return;
                }

                if (channel.State != ChannelState.Paused)
                {
                    Log($"[多通道] 恢复失败: 通道 {channelId} 当前不在暂停状态");
                    return;
                }

                // 恢复通道中的所有轴
                foreach (var axisName in channel.Config.AxisNames)
                {
                    if (_axes.TryGetValue(axisName, out var axis))
                    {
                        axis.Resume();
                    }
                }

                channel.State = ChannelState.Running;
                Log($"[通道{channelId}] 已恢复运行");
            }
        }

        /// <summary>
        /// 停止通道 — 停止指定通道的程序执行并停止所有相关轴。
        /// </summary>
        /// <param name="channelId">通道编号。</param>
        public void StopChannel(int channelId)
        {
            lock (_channelLock)
            {
                if (!_channels.TryGetValue(channelId, out var channel))
                {
                    Log($"[多通道] 停止失败: 通道 {channelId} 不存在");
                    return;
                }

                // 取消执行任务
                channel._cts?.Cancel();

                // 停止通道中的所有轴
                foreach (var axisName in channel.Config.AxisNames)
                {
                    if (_axes.TryGetValue(axisName, out var axis))
                    {
                        axis.Stop();
                    }
                }

                channel.State = ChannelState.Idle;
                Log($"[通道{channelId}] 已停止");
            }
        }

        /// <summary>
        /// 停止所有通道 — 紧急停止所有正在运行的通道。
        /// </summary>
        public void StopAllChannels()
        {
            Log("[多通道] 停止所有通道");

            // 获取所有通道编号的快照，避免在迭代中修改字典
            List<int> channelIds;
            lock (_channelLock)
            {
                channelIds = _channels.Keys.ToList();
            }

            foreach (var id in channelIds)
            {
                StopChannel(id);
            }

            Log("[多通道] 所有通道已停止");
        }

        // ==================== 同步屏障 ====================

        /// <summary>
        /// 添加同步屏障 — 注册一个跨通道同步点。
        /// 屏障定义了哪些通道需要在同一时刻同步。
        /// </summary>
        /// <param name="barrier">同步屏障定义。</param>
        public void AddSyncBarrier(SyncBarrier barrier)
        {
            lock (_barrierLock)
            {
                _barriers[barrier.Id] = barrier;
                // 初始化计数器为 0
                _barrierCounters[barrier.Id] = 0;
                // 创建完成信号源
                _barrierSignals[barrier.Id] = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                Log($"[多通道] 添加同步屏障: {barrier.Id}, 通道: [{string.Join(", ", barrier.ChannelIds)}], 超时: {barrier.TimeoutMs}ms");
            }
        }

        /// <summary>
        /// 等待同步屏障 — 当前通道到达屏障并等待其他通道。
        ///
        /// 实现原理：
        ///   1. 每个到达的通道将计数器加 1
        ///   2. 当计数器等于屏障中的通道总数时，触发 TaskCompletionSource
        ///   3. 所有等待的通道同时被释放继续执行
        ///   4. 屏障被重置，可以在下次循环中再次使用
        /// </summary>
        /// <param name="barrierId">屏障编号。</param>
        /// <param name="channelId">当前通道编号。</param>
        /// <param name="ct">取消令牌。</param>
        public async Task WaitForSyncBarrier(int barrierId, int channelId, CancellationToken ct)
        {
            TaskCompletionSource<bool> signal;
            int requiredCount;
            int timeoutMs;

            lock (_barrierLock)
            {
                if (!_barriers.TryGetValue(barrierId, out var barrier))
                {
                    Log($"[通道{channelId}] 同步屏障 {barrierId} 不存在，跳过等待");
                    return;
                }

                requiredCount = barrier.ChannelIds.Count;
                timeoutMs = barrier.TimeoutMs;

                // 当前通道到达，计数器加 1
                _barrierCounters[barrierId]++;
                int currentCount = _barrierCounters[barrierId];
                signal = _barrierSignals[barrierId];

                Log($"[通道{channelId}] 到达同步屏障 {barrierId} ({currentCount}/{requiredCount})");

                // 检查是否所有通道都已到达
                if (currentCount >= requiredCount)
                {
                    // 所有通道已到达，触发信号释放所有等待者
                    signal.TrySetResult(true);

                    // 重置屏障以供下次使用
                    _barrierCounters[barrierId] = 0;
                    _barrierSignals[barrierId] = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);

                    Log($"[多通道] 同步屏障 {barrierId} 已释放，所有通道继续执行");
                    return;
                }
            }

            // 等待其他通道到达（支持超时和取消）
            if (timeoutMs > 0)
            {
                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                try
                {
                    await signal.Task.WaitAsync(linkedCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    Log($"[通道{channelId}] 同步屏障 {barrierId} 等待超时（{timeoutMs}ms）");
                    throw new TimeoutException(
                        $"通道 {channelId} 在同步屏障 {barrierId} 上等待超时（{timeoutMs}ms）");
                }
            }
            else
            {
                // 无超时限制，仅响应取消令牌
                await signal.Task.WaitAsync(ct);
            }
        }

        // ==================== 状态查询 ====================

        /// <summary>
        /// 获取所有通道状态 — 返回每个通道的运行状态和执行进度。
        /// </summary>
        /// <returns>通道编号到 (状态, 当前行, 总行数) 的映射。</returns>
        public Dictionary<int, (ChannelState State, int CurrentLine, int TotalLines)> GetAllChannelStatus()
        {
            var status = new Dictionary<int, (ChannelState, int, int)>();

            lock (_channelLock)
            {
                foreach (var kvp in _channels)
                {
                    var ch = kvp.Value;
                    status[kvp.Key] = (ch.State, ch.CurrentLine, ch.TotalLines);
                }
            }

            return status;
        }

        // ==================== 私有方法 ====================

        /// <summary>
        /// 执行通道程序 — 单个通道的 G 代码执行主循环。
        ///
        /// 执行逻辑：
        ///   1. 解析 G 代码程序为指令列表
        ///   2. 逐条遍历指令
        ///   3. 对于 G0/G1（直线运动）：对涉及的轴调用 MoveTo，然后等待轴到位
        ///   4. 对于其他指令：记录日志并跳过
        ///   5. 遇到 M2/M30 时结束程序
        ///   6. 支持取消令牌中断
        /// </summary>
        /// <param name="channel">通道实例。</param>
        /// <param name="ct">取消令牌。</param>
        private async Task ExecuteChannelProgramAsync(MotionChannel channel, CancellationToken ct)
        {
            // 解析 G 代码程序
            var commands = channel.Parser.Parse(channel.CurrentProgram);
            int channelId = channel.Config.Id;

            for (int i = 0; i < commands.Count; i++)
            {
                // 检查取消请求
                ct.ThrowIfCancellationRequested();

                // 等待通道从暂停状态恢复
                while (channel.State == ChannelState.Paused)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(10, ct);
                }

                var cmd = commands[i];
                channel.CurrentLine = i + 1;

                switch (cmd.Type)
                {
                    case GCodeTypeV2.G0:
                    case GCodeTypeV2.G1:
                        // 直线运动 — 对每个涉及的轴发出移动指令
                        await ExecuteLinearMoveAsync(channel, cmd, ct);
                        break;

                    case GCodeTypeV2.M2:
                    case GCodeTypeV2.M30:
                        // 程序结束指令
                        Log($"[通道{channelId}] 行{cmd.LineNumber}: {cmd.Type} 程序结束");
                        return;

                    case GCodeTypeV2.M0:
                        // 程序暂停指令 — 将通道置为暂停状态
                        Log($"[通道{channelId}] 行{cmd.LineNumber}: M0 程序暂停，等待恢复...");
                        channel.State = ChannelState.Paused;
                        while (channel.State == ChannelState.Paused)
                        {
                            ct.ThrowIfCancellationRequested();
                            await Task.Delay(10, ct);
                        }
                        break;

                    default:
                        // 其他指令 — 记录日志并跳过
                        Log($"[通道{channelId}] 行{cmd.LineNumber}: {cmd.Type} '{cmd.Raw}' — 跳过（仅支持 G0/G1 直线运动）");
                        break;
                }
            }
        }

        /// <summary>
        /// 执行直线运动 — 对 G0/G1 指令中涉及的轴发出 MoveTo 命令并等待到位。
        ///
        /// 处理流程：
        ///   1. 筛选出属于当前通道的轴位置数据
        ///   2. 对每个轴调用 MoveTo（非阻塞，轴在后台线程中执行运动）
        ///   3. 等待所有轴运动完成（轮询状态直到 Idle）
        /// </summary>
        /// <param name="channel">通道实例。</param>
        /// <param name="cmd">G 代码指令。</param>
        /// <param name="ct">取消令牌。</param>
        private async Task ExecuteLinearMoveAsync(MotionChannel channel, GCodeCommandV2 cmd, CancellationToken ct)
        {
            int channelId = channel.Config.Id;
            var axesToMove = new List<string>();

            // 遍历指令中的轴位置，筛选属于本通道的轴
            foreach (var kvp in cmd.AxisPositions)
            {
                string axisName = kvp.Key;
                double targetPos = kvp.Value;

                // 检查该轴是否属于本通道
                if (!channel.Config.AxisNames.Contains(axisName))
                {
                    continue;
                }

                // 检查轴是否已注册
                if (!_axes.TryGetValue(axisName, out var axis))
                {
                    Log($"[通道{channelId}] 警告: 轴 {axisName} 未在控制器中注册，跳过");
                    continue;
                }

                // 发出移动指令
                bool moveStarted = axis.MoveTo(targetPos);
                if (moveStarted)
                {
                    axesToMove.Add(axisName);
                }
                else
                {
                    Log($"[通道{channelId}] 警告: 轴 {axisName} 无法启动移动（当前状态: {axis.State}）");
                }
            }

            // 等待所有轴运动完成
            if (axesToMove.Count > 0)
            {
                Log($"[通道{channelId}] 行{cmd.LineNumber}: {cmd.Type} 移动轴 [{string.Join(", ", axesToMove)}]");
                await WaitForAxesAsync(axesToMove, ct);
            }
        }

        /// <summary>
        /// 等待轴运动完成 — 轮询指定轴直到全部回到空闲状态。
        /// 轮询间隔为 10ms，以平衡响应速度和 CPU 开销。
        /// </summary>
        /// <param name="axisNames">需要等待的轴名称列表。</param>
        /// <param name="ct">取消令牌。</param>
        private async Task WaitForAxesAsync(IEnumerable<string> axisNames, CancellationToken ct)
        {
            var nameList = axisNames.ToList();

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // 检查所有轴是否都已回到空闲状态
                bool allIdle = true;
                foreach (var name in nameList)
                {
                    if (_axes.TryGetValue(name, out var axis))
                    {
                        if (axis.State != AxisState.Idle)
                        {
                            allIdle = false;
                            break;
                        }
                    }
                }

                if (allIdle)
                {
                    return;
                }

                // 等待 10ms 后重新检查
                await Task.Delay(10, ct);
            }
        }

        /// <summary>
        /// 记录日志 — 触发 MessageLogged 事件将消息传递给外部。
        /// </summary>
        /// <param name="msg">日志消息文本。</param>
        private void Log(string msg)
        {
            MessageLogged?.Invoke(this, msg);
        }
    }
}
