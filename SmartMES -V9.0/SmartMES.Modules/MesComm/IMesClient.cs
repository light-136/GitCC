namespace SmartMES.Modules.MesComm
{
    // ============================================================
    // MES通信接口定义
    // 工厂MES系统（Manufacturing Execution System）通信抽象层
    // 支持 HTTP REST / MQTT / OPC-UA 三种协议，面向接口编程
    // ============================================================

    /// <summary>MES工单数据模型</summary>
    public class MesWorkOrder
    {
        public string OrderId { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int PlannedQty { get; set; }
        public int ActualQty { get; set; }
        public string Status { get; set; } = "Pending"; // Pending/Running/Done
        public DateTime CreateTime { get; set; } = DateTime.Now;
    }

    /// <summary>MES上报结果</summary>
    public class MesReportResult
    {
        public string OrderId { get; set; } = string.Empty;
        public int Qty { get; set; }
        public bool IsPass { get; set; }
        public string Remark { get; set; } = string.Empty;
        public DateTime ReportTime { get; set; } = DateTime.Now;
    }

    /// <summary>MES通信接口：抽象不同协议下的统一MES交互能力</summary>
    public interface IMesClient
    {
        /// <summary>协议名称（HTTP/MQTT/OPC-UA）</summary>
        string ProtocolName { get; }
        /// <summary>当前连接状态</summary>
        bool IsConnected { get; }
        /// <summary>建立与MES端连接</summary>
        Task ConnectAsync();
        /// <summary>断开与MES端连接</summary>
        Task DisconnectAsync();
        /// <summary>获取工单列表</summary>
        Task<List<MesWorkOrder>> GetWorkOrdersAsync();
        /// <summary>上报生产结果</summary>
        Task<bool> ReportResultAsync(MesReportResult result);
        /// <summary>订阅实时数据推送（OPC-UA / MQTT）</summary>
        void Subscribe(Action<string, object> onDataReceived);
        event EventHandler<string>? MessageReceived;
    }
}
