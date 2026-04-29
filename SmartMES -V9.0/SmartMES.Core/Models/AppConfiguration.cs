namespace SmartMES.Core.Models
{
    /// <summary>
    /// 扩展版系统配置模型（分层配置中心）。
    /// 将配置按功能域分组，支持：系统基础 / 通信设备 / Modbus / 数据库 / 日志 / MES / 报警阈值 / 界面。
    /// 与 SystemSettings 并存，逐步过渡，不破坏现有代码。
    /// </summary>
    public class AppConfiguration
    {
        // ════════ 系统基础配置 ════════
        /// <summary>系统名称（显示在标题栏）</summary>
        public string SystemName { get; set; } = "SmartMES 智能制造执行系统";

        /// <summary>系统版本号</summary>
        public string Version { get; set; } = "3.2.0";

        /// <summary>运行模式：Simulation（仿真）/ Real（真实硬件）</summary>
        public string RunMode { get; set; } = "Simulation";

        /// <summary>数据采集间隔（毫秒）</summary>
        public int DataSamplingIntervalMs { get; set; } = 1000;

        // ════════ TCP 通信配置 ════════
        /// <summary>TCP 服务器 IP 地址</summary>
        public string TcpServerIp { get; set; } = "127.0.0.1";

        /// <summary>TCP 服务器端口</summary>
        public int TcpServerPort { get; set; } = 9000;

        /// <summary>TCP 连接超时（毫秒）</summary>
        public int TcpTimeoutMs { get; set; } = 5000;

        /// <summary>TCP 自动重连间隔（秒）</summary>
        public int TcpReconnectIntervalSec { get; set; } = 10;

        // ════════ 串口通信配置 ════════
        /// <summary>串口号（如 COM1）</summary>
        public string SerialPortName { get; set; } = "COM1";

        /// <summary>串口波特率</summary>
        public int SerialBaudRate { get; set; } = 9600;

        /// <summary>串口数据位</summary>
        public int SerialDataBits { get; set; } = 8;

        /// <summary>串口停止位（1 / 2）</summary>
        public int SerialStopBits { get; set; } = 1;

        // ════════ Modbus TCP 配置 ════════
        /// <summary>Modbus TCP 设备 IP 地址</summary>
        public string ModbusHostIp { get; set; } = "192.168.1.100";

        /// <summary>Modbus TCP 端口（标准 502）</summary>
        public int ModbusPort { get; set; } = 502;

        /// <summary>Modbus 从站地址</summary>
        public byte ModbusUnitId { get; set; } = 1;

        /// <summary>Modbus 心跳间隔（秒）</summary>
        public int ModbusHeartbeatSec { get; set; } = 10;

        // ════════ 数据库配置 ════════
        /// <summary>数据库类型：SQLite / MySQL / SqlServer</summary>
        public string DatabaseType { get; set; } = "SQLite";

        /// <summary>SQLite 数据库文件路径</summary>
        public string SqliteDbPath { get; set; } = "SmartMES.db";

        /// <summary>MySQL/SQL Server 连接字符串（非 SQLite 时使用）</summary>
        public string DatabaseConnectionString { get; set; } = "";

        /// <summary>历史数据保留天数（超出则归档）</summary>
        public int DataRetentionDays { get; set; } = 90;

        // ════════ 日志配置 ════════
        /// <summary>日志输出目录</summary>
        public string LogDirectory { get; set; } = "Logs";

        /// <summary>日志保留天数</summary>
        public int LogRetentionDays { get; set; } = 30;

        /// <summary>内存中最大日志条数</summary>
        public int MaxLogEntries { get; set; } = 1000;

        /// <summary>日志最低级别：Debug / Info / Warning / Error</summary>
        public string MinLogLevel { get; set; } = "Info";

        // ════════ MES 通信配置 ════════
        /// <summary>MES 接口协议：HTTP / MQTT / OPCUA</summary>
        public string MesProtocol { get; set; } = "HTTP";

        /// <summary>MES HTTP 接口基础地址</summary>
        public string MesHttpBaseUrl { get; set; } = "http://mes-server:8080/api/";

        /// <summary>MQTT Broker 地址</summary>
        public string MqttBrokerHost { get; set; } = "mqtt-broker";

        /// <summary>MQTT Broker 端口</summary>
        public int MqttBrokerPort { get; set; } = 1883;

        /// <summary>OPC UA 服务器端点地址</summary>
        public string OpcUaEndpoint { get; set; } = "opc.tcp://localhost:4840";

        /// <summary>MES 接口超时（秒）</summary>
        public int MesTimeoutSec { get; set; } = 30;

        /// <summary>MES 请求失败重试次数</summary>
        public int MesRetryCount { get; set; } = 3;

        // ════════ 报警阈值配置 ════════
        /// <summary>温度报警上限（℃）</summary>
        public double TemperatureAlarmThreshold { get; set; } = 85.0;

        /// <summary>压力报警上限（MPa）</summary>
        public double PressureAlarmThreshold { get; set; } = 10.0;

        /// <summary>转速报警上限（rpm）</summary>
        public double SpeedAlarmThreshold { get; set; } = 3000.0;

        // ════════ 界面配置 ════════
        /// <summary>图表历史数据点数量</summary>
        public int ChartHistoryPoints { get; set; } = 60;

        /// <summary>UI 刷新间隔（毫秒）</summary>
        public int UiRefreshIntervalMs { get; set; } = 100;
    }
}
