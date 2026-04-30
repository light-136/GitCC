// ============================================================
// 文件：SecsGemModels.cs
// 用途：SECS/GEM 半导体通信领域模型
// 标准：SEMI E5（SECS-II）、E30（GEM）、E37（HSMS）
// 设计思路：
//   定义 HSMS 传输层、SECS-II 消息编码、GEM 合规层
//   所需的全部数据结构。SecsItem 采用递归树结构，
//   精确映射 SECS-II 的嵌套列表数据模型。
// ============================================================

namespace SmartMES.Core.Models
{
    // ======================== HSMS 传输层（SEMI E37）========================

    /// <summary>
    /// HSMS 连接状态 — 对应 SEMI E37 状态机。
    /// </summary>
    public enum HsmsState
    {
        /// <summary>未连接（TCP 未建立）。</summary>
        NotConnected,

        /// <summary>已连接但未选择（TCP 已建立，等待 Select 握手）。</summary>
        NotSelected,

        /// <summary>已选择（Select 握手完成，可以收发数据消息）。</summary>
        Selected
    }

    /// <summary>
    /// HSMS 消息类型 — SType 字段值。
    /// </summary>
    public enum HsmsMessageType : byte
    {
        /// <summary>SECS-II 数据消息。</summary>
        DataMessage = 0,

        /// <summary>Select 请求。</summary>
        SelectReq = 1,

        /// <summary>Select 响应。</summary>
        SelectRsp = 2,

        /// <summary>Deselect 请求。</summary>
        DeselectReq = 3,

        /// <summary>Deselect 响应。</summary>
        DeselectRsp = 4,

        /// <summary>Linktest 请求（心跳）。</summary>
        LinktestReq = 5,

        /// <summary>Linktest 响应。</summary>
        LinktestRsp = 6,

        /// <summary>Reject 请求。</summary>
        RejectReq = 7,

        /// <summary>Separate 请求（断开）。</summary>
        SeparateReq = 9
    }

    // ======================== GEM 状态模型（SEMI E30）========================

    /// <summary>
    /// GEM 通信状态 — 描述设备与主机之间的通信链路状态。
    /// </summary>
    public enum GemCommunicationState
    {
        /// <summary>通信功能禁用。</summary>
        Disabled,

        /// <summary>通信功能启用，等待建立通信。</summary>
        WaitCommunicating,

        /// <summary>通信已建立。</summary>
        Communicating,

        /// <summary>通信中断。</summary>
        NotCommunicating
    }

    /// <summary>
    /// GEM 控制状态 — 描述设备的在线/离线控制模式。
    /// </summary>
    public enum GemControlState
    {
        /// <summary>设备离线。</summary>
        EquipmentOffline,

        /// <summary>正在尝试上线。</summary>
        AttemptOnline,

        /// <summary>主机离线（主机拒绝了上线请求）。</summary>
        HostOffline,

        /// <summary>在线本地模式（操作员控制）。</summary>
        OnlineLocal,

        /// <summary>在线远程模式（主机控制）。</summary>
        OnlineRemote
    }

    // ======================== SECS-II 数据模型（SEMI E5）========================

    /// <summary>
    /// SECS-II 数据项类型 — 对应 SEMI E5 定义的格式码。
    /// 高6位为格式码，低2位为长度字节数（编码时计算）。
    /// </summary>
    public enum SecsItemType : byte
    {
        /// <summary>列表（包含子项的容器）。</summary>
        List = 0x00,

        /// <summary>二进制数据。</summary>
        Binary = 0x20,

        /// <summary>布尔值。</summary>
        Boolean = 0x24,

        /// <summary>ASCII 字符串。</summary>
        Ascii = 0x40,

        /// <summary>JIS-8 字符串。</summary>
        Jis8 = 0x44,

        /// <summary>8字节有符号整数。</summary>
        I8 = 0x60,

        /// <summary>1字节有符号整数。</summary>
        I1 = 0x64,

        /// <summary>2字节有符号整数。</summary>
        I2 = 0x68,

        /// <summary>4字节有符号整数。</summary>
        I4 = 0x70,

        /// <summary>8字节浮点数（双精度）。</summary>
        F8 = 0x80,

        /// <summary>4字节浮点数（单精度）。</summary>
        F4 = 0x90,

        /// <summary>8字节无符号整数。</summary>
        U8 = 0xA0,

        /// <summary>1字节无符号整数。</summary>
        U1 = 0xA4,

        /// <summary>2字节无符号整数。</summary>
        U2 = 0xA8,

        /// <summary>4字节无符号整数。</summary>
        U4 = 0xB0
    }

    /// <summary>
    /// SECS-II 数据项 — 递归树结构，是 SECS-II 消息体的基本单元。
    /// 叶节点存储具体数据值，List 节点包含子项列表。
    /// </summary>
    public class SecsItem
    {
        /// <summary>数据项类型。</summary>
        public SecsItemType Type { get; set; }

        /// <summary>
        /// 数据值（叶节点）。
        /// 类型对应关系：
        ///   Ascii → string, Binary → byte[],
        ///   Boolean → bool, I1 → sbyte, I2 → short, I4 → int, I8 → long,
        ///   U1 → byte, U2 → ushort, U4 → uint, U8 → ulong,
        ///   F4 → float, F8 → double
        /// 数组类型：I4 可以是 int[]，U2 可以是 ushort[] 等。
        /// </summary>
        public object? Value { get; set; }

        /// <summary>子项列表（仅 List 类型使用）。</summary>
        public List<SecsItem> Children { get; set; } = new();

        // ---- 工厂方法：简化创建过程 ----

        /// <summary>创建列表项。</summary>
        public static SecsItem CreateList(params SecsItem[] children) =>
            new() { Type = SecsItemType.List, Children = new List<SecsItem>(children) };

        /// <summary>创建 ASCII 字符串项。</summary>
        public static SecsItem CreateAscii(string value) =>
            new() { Type = SecsItemType.Ascii, Value = value };

        /// <summary>创建布尔项。</summary>
        public static SecsItem CreateBoolean(bool value) =>
            new() { Type = SecsItemType.Boolean, Value = value };

        /// <summary>创建二进制数据项。</summary>
        public static SecsItem CreateBinary(byte[] value) =>
            new() { Type = SecsItemType.Binary, Value = value };

        /// <summary>创建 1 字节无符号整数项。</summary>
        public static SecsItem CreateU1(byte value) =>
            new() { Type = SecsItemType.U1, Value = value };

        /// <summary>创建 2 字节无符号整数项。</summary>
        public static SecsItem CreateU2(ushort value) =>
            new() { Type = SecsItemType.U2, Value = value };

        /// <summary>创建 4 字节无符号整数项。</summary>
        public static SecsItem CreateU4(uint value) =>
            new() { Type = SecsItemType.U4, Value = value };

        /// <summary>创建 8 字节无符号整数项。</summary>
        public static SecsItem CreateU8(ulong value) =>
            new() { Type = SecsItemType.U8, Value = value };

        /// <summary>创建 1 字节有符号整数项。</summary>
        public static SecsItem CreateI1(sbyte value) =>
            new() { Type = SecsItemType.I1, Value = value };

        /// <summary>创建 2 字节有符号整数项。</summary>
        public static SecsItem CreateI2(short value) =>
            new() { Type = SecsItemType.I2, Value = value };

        /// <summary>创建 4 字节有符号整数项。</summary>
        public static SecsItem CreateI4(int value) =>
            new() { Type = SecsItemType.I4, Value = value };

        /// <summary>创建 8 字节有符号整数项。</summary>
        public static SecsItem CreateI8(long value) =>
            new() { Type = SecsItemType.I8, Value = value };

        /// <summary>创建单精度浮点项。</summary>
        public static SecsItem CreateF4(float value) =>
            new() { Type = SecsItemType.F4, Value = value };

        /// <summary>创建双精度浮点项。</summary>
        public static SecsItem CreateF8(double value) =>
            new() { Type = SecsItemType.F8, Value = value };

        /// <summary>
        /// 返回 SML（SECS Message Language）文本表示。
        /// 例如：&lt;L [2] &lt;A "MDLN"&gt; &lt;U4 12345&gt;&gt;
        /// </summary>
        public override string ToString()
        {
            if (Type == SecsItemType.List)
                return $"<L [{Children.Count}] {string.Join(" ", Children.Select(c => c.ToString()))}>";
            if (Type == SecsItemType.Ascii)
                return $"<A \"{Value}\">";
            return $"<{Type} {Value}>";
        }
    }

    /// <summary>
    /// SECS-II 消息 — 由 Stream/Function 标识的完整消息。
    /// </summary>
    public class SecsMessage
    {
        /// <summary>消息流号（Stream），标识消息类别。</summary>
        public int Stream { get; set; }

        /// <summary>消息功能号（Function），标识具体功能。</summary>
        public int Function { get; set; }

        /// <summary>W-Bit（Wait Bit），为 true 表示期望回复。</summary>
        public bool WBit { get; set; }

        /// <summary>系统字节（事务标识符），用于匹配请求与响应。</summary>
        public uint SystemBytes { get; set; }

        /// <summary>消息体（SECS-II 数据项树）。</summary>
        public SecsItem? Body { get; set; }

        /// <summary>消息时间戳。</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>消息描述（如 "S1F1"）。</summary>
        public string Description => $"S{Stream}F{Function}";

        /// <summary>
        /// 创建回复消息（Function+1，相同 SystemBytes）。
        /// </summary>
        public SecsMessage CreateReply(SecsItem? body = null) => new()
        {
            Stream = Stream,
            Function = Function + 1,
            WBit = false,
            SystemBytes = SystemBytes,
            Body = body
        };
    }

    // ======================== GEM 数据管理 ========================

    /// <summary>
    /// 状态变量（Status Variable, SV）— 设备运行时的动态数据。
    /// 对应 SEMI E30 中的 SV 概念。
    /// </summary>
    public class StatusVariable
    {
        /// <summary>变量 ID（SVID）。</summary>
        public uint Id { get; set; }

        /// <summary>变量名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>当前值。</summary>
        public object Value { get; set; } = 0;

        /// <summary>单位。</summary>
        public string Units { get; set; } = string.Empty;

        /// <summary>SECS-II 数据类型。</summary>
        public SecsItemType DataType { get; set; } = SecsItemType.I4;
    }

    /// <summary>
    /// 设备常量（Equipment Constant, EC）— 可由主机修改的设备参数。
    /// </summary>
    public class EquipmentConstant
    {
        /// <summary>常量 ID（ECID）。</summary>
        public uint Id { get; set; }

        /// <summary>常量名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>当前值。</summary>
        public object Value { get; set; } = 0;

        /// <summary>最小允许值。</summary>
        public double MinValue { get; set; } = double.MinValue;

        /// <summary>最大允许值。</summary>
        public double MaxValue { get; set; } = double.MaxValue;

        /// <summary>单位。</summary>
        public string Units { get; set; } = string.Empty;

        /// <summary>SECS-II 数据类型。</summary>
        public SecsItemType DataType { get; set; } = SecsItemType.I4;
    }

    /// <summary>
    /// 采集事件（Collection Event, CE）— 设备上报给主机的事件。
    /// </summary>
    public class CollectionEvent
    {
        /// <summary>事件 ID（CEID）。</summary>
        public uint Id { get; set; }

        /// <summary>事件名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>是否启用上报。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>关联的报告 ID 列表。</summary>
        public List<uint> LinkedReportIds { get; set; } = new();
    }

    /// <summary>
    /// 报告定义（Report Definition）— 定义一组要上报的变量。
    /// </summary>
    public class ReportDefinition
    {
        /// <summary>报告 ID（RPTID）。</summary>
        public uint Id { get; set; }

        /// <summary>包含的变量 ID 列表（SVID 或 DVID）。</summary>
        public List<uint> VariableIds { get; set; } = new();
    }

    /// <summary>
    /// SECS 告警（Alarm）— 设备异常状态通知。
    /// </summary>
    public class SecsAlarm
    {
        /// <summary>告警 ID（ALID）。</summary>
        public uint Id { get; set; }

        /// <summary>告警名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>是否处于激活状态。</summary>
        public bool IsSet { get; set; }

        /// <summary>告警文本描述。</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>告警等级（ALCD 高4位）。</summary>
        public byte AlarmCode { get; set; }

        /// <summary>是否启用上报。</summary>
        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// HSMS 定时器配置 — 各超时参数。
    /// </summary>
    public class HsmsTimerConfig
    {
        /// <summary>T3：回复超时（等待对方回复数据消息的最长时间）。</summary>
        public TimeSpan T3 { get; set; } = TimeSpan.FromSeconds(45);

        /// <summary>T5：连接分离超时（断开后重连等待时间）。</summary>
        public TimeSpan T5 { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>T6：控制消息超时（等待控制消息回复的最长时间）。</summary>
        public TimeSpan T6 { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>T7：未选择超时（连接后等待 Select 的最长时间）。</summary>
        public TimeSpan T7 { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>T8：字节间超时（接收消息时字节间的最大间隔）。</summary>
        public TimeSpan T8 { get; set; } = TimeSpan.FromSeconds(5);
    }
}
