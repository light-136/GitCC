// ============================================================
// 文件：GemRemoteCommandHandler.cs
// 用途：GEM 远程命令处理器 — 接收和执行主机发送的远程命令
// 标准：SEMI E30 — 远程命令（S2F41/S2F42）
// 设计思路：
//   主机通过 S2F41 向设备发送远程命令，设备执行后通过 S2F42 回复。
//   命令处理器采用注册-回调模式：
//   1. 设备预注册可执行的命令及其处理函数
//   2. 收到 S2F41 后查找并执行对应的处理函数
//   3. 返回 HCACK（主机命令确认码）和参数确认
//
//   HCACK 返回码（SEMI E30 标准）：
//     0 = 命令执行成功
//     1 = 无效命令（未注册）
//     2 = 当前无法执行
//     3 = 参数错误
//     4 = 已确认但未开始
//     5 = 已在目标状态
//     6 = 对象不存在
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Services.SecsGem
{
    /// <summary>
    /// 远程命令执行结果。
    /// </summary>
    public class RemoteCommandResult
    {
        /// <summary>HCACK 返回码。</summary>
        public byte HcAck { get; set; }

        /// <summary>参数确认列表（参数名 → CPACK码）。</summary>
        public Dictionary<string, byte> ParameterAcks { get; set; } = new();
    }

    /// <summary>
    /// 远程命令定义 — 描述一个可远程执行的命令。
    /// </summary>
    public class RemoteCommandDefinition
    {
        /// <summary>命令名称（RCMD）。</summary>
        public string CommandName { get; set; } = "";

        /// <summary>命令描述。</summary>
        public string Description { get; set; } = "";

        /// <summary>支持的参数名称列表。</summary>
        public List<string> SupportedParameters { get; set; } = new();

        /// <summary>
        /// 命令处理函数。
        /// 输入：参数字典（名称→值）
        /// 输出：RemoteCommandResult
        /// </summary>
        public Func<Dictionary<string, string>, RemoteCommandResult>? Handler { get; set; }
    }

    /// <summary>
    /// GEM 远程命令处理器 — 注册和执行主机远程命令。
    ///
    /// 使用方式：
    ///   handler.RegisterCommand("START", "启动加工", ["LOT_ID"],
    ///       args => { /* 执行启动逻辑 */ return new RemoteCommandResult { HcAck = 0 }; });
    ///
    /// 消息流程：
    ///   主机发送 S2F41 → 解析 RCMD + CPNAME/CPVAL → 查找处理器 → 执行 → 回复 S2F42
    /// </summary>
    public class GemRemoteCommandHandler
    {
        // 已注册的命令
        private readonly Dictionary<string, RemoteCommandDefinition> _commands = new();
        private readonly object _lock = new();

        /// <summary>命令执行事件（命令名, 参数, 结果）。</summary>
        public event EventHandler<(string Command, Dictionary<string, string> Params, RemoteCommandResult Result)>?
            CommandExecuted;

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        // ========== 命令注册 ==========

        /// <summary>
        /// 注册远程命令。
        /// </summary>
        /// <param name="name">命令名称。</param>
        /// <param name="description">命令描述。</param>
        /// <param name="parameters">支持的参数名称列表。</param>
        /// <param name="handler">命令处理函数。</param>
        public void RegisterCommand(string name, string description,
                                     List<string>? parameters,
                                     Func<Dictionary<string, string>, RemoteCommandResult> handler)
        {
            lock (_lock)
            {
                _commands[name.ToUpperInvariant()] = new RemoteCommandDefinition
                {
                    CommandName = name,
                    Description = description,
                    SupportedParameters = parameters ?? new(),
                    Handler = handler
                };
                Log($"[GEM远程命令] 注册命令：{name} — {description}");
            }
        }

        /// <summary>
        /// 注册远程命令（简化版，无参数）。
        /// </summary>
        public void RegisterCommand(string name, string description,
                                     Func<RemoteCommandResult> handler)
        {
            RegisterCommand(name, description, null, _ => handler());
        }

        // ========== 命令执行 ==========

        /// <summary>
        /// 处理 S2F41 远程命令 — 解析并执行命令。
        /// </summary>
        /// <param name="body">S2F41 消息体 SecsItem。</param>
        /// <returns>S2F42 响应消息体。</returns>
        public SecsItem HandleS2F41(SecsItem body)
        {
            // S2F41 格式：
            // <L [2]
            //   <A RCMD>        ; 命令名称
            //   <L [n]          ; 参数列表
            //     <L [2]
            //       <A CPNAME>  ; 参数名
            //       <A CPVAL>   ; 参数值
            //     >
            //   >
            // >

            string rcmd = "";
            var parameters = new Dictionary<string, string>();

            if (body.Type == SecsItemType.List && body.Children.Count >= 2)
            {
                rcmd = body.Children[0].Value?.ToString() ?? "";

                var paramList = body.Children[1];
                if (paramList.Type == SecsItemType.List)
                {
                    foreach (var param in paramList.Children)
                    {
                        if (param.Type == SecsItemType.List && param.Children.Count >= 2)
                        {
                            string cpName = param.Children[0].Value?.ToString() ?? "";
                            string cpVal = param.Children[1].Value?.ToString() ?? "";
                            parameters[cpName] = cpVal;
                        }
                    }
                }
            }

            Log($"[GEM远程命令] 收到命令：{rcmd}，参数数={parameters.Count}");

            // 执行命令
            var result = ExecuteCommand(rcmd, parameters);

            // 构建 S2F42 响应
            return BuildS2F42Response(result, parameters);
        }

        /// <summary>
        /// 执行远程命令。
        /// </summary>
        public RemoteCommandResult ExecuteCommand(string commandName,
                                                    Dictionary<string, string> parameters)
        {
            lock (_lock)
            {
                string key = commandName.ToUpperInvariant();

                // 检查命令是否已注册
                if (!_commands.TryGetValue(key, out var cmd))
                {
                    Log($"[GEM远程命令] 无效命令：{commandName}");
                    return new RemoteCommandResult { HcAck = 1 }; // 无效命令
                }

                // 检查参数
                if (cmd.Handler == null)
                {
                    return new RemoteCommandResult { HcAck = 2 }; // 当前无法执行
                }

                try
                {
                    var result = cmd.Handler(parameters);
                    Log($"[GEM远程命令] 执行 {commandName}，HCACK={result.HcAck}");

                    CommandExecuted?.Invoke(this, (commandName, parameters, result));
                    return result;
                }
                catch (Exception ex)
                {
                    Log($"[GEM远程命令] 执行异常：{ex.Message}");
                    return new RemoteCommandResult { HcAck = 2 };
                }
            }
        }

        // ========== 消息构建 ==========

        /// <summary>
        /// 构建 S2F42 响应消息体。
        /// 格式：
        ///   &lt;L [2]
        ///     &lt;B HCACK&gt;     ; 命令确认码
        ///     &lt;L [n]        ; 参数确认列表
        ///       &lt;L [2]
        ///         &lt;A CPNAME&gt;
        ///         &lt;B CPACK&gt;  ; 0=成功
        ///       &gt;
        ///     &gt;
        ///   &gt;
        /// </summary>
        private SecsItem BuildS2F42Response(RemoteCommandResult result,
                                             Dictionary<string, string> parameters)
        {
            var root = SecsItem.CreateList();

            // HCACK
            root.Children.Add(SecsItem.CreateBinary(new[] { result.HcAck }));

            // 参数确认列表
            var paramAcks = SecsItem.CreateList();
            foreach (var param in parameters)
            {
                var ack = SecsItem.CreateList();
                ack.Children.Add(SecsItem.CreateAscii(param.Key));

                byte cpack = result.ParameterAcks.GetValueOrDefault(param.Key, (byte)0);
                ack.Children.Add(SecsItem.CreateBinary(new[] { cpack }));

                paramAcks.Children.Add(ack);
            }

            root.Children.Add(paramAcks);
            return root;
        }

        /// <summary>
        /// 获取所有已注册的命令列表。
        /// </summary>
        public List<RemoteCommandDefinition> GetRegisteredCommands()
        {
            lock (_lock) { return _commands.Values.ToList(); }
        }

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);
    }
}
