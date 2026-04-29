namespace SmartMES.Core.Models
{
    /// <summary>
    /// 系统配置模型
    /// 包含所有可配置的系统参数，支持JSON序列化保存
    /// </summary>
    public class SystemSettings
    {
        // =========== 数据采集参数 ===========

        /// <summary>数据采集间隔（毫秒），默认1000ms即1秒</summary>
        public int DataSamplingIntervalMs { get; set; } = 1000;

        /// <summary>温度报警上限阈值（℃）</summary>
        public double TemperatureAlarmThreshold { get; set; } = 85.0;

        /// <summary>压力报警上限阈值（MPa）</summary>
        public double PressureAlarmThreshold { get; set; } = 10.0;

        /// <summary>速度报警上限阈值（rpm）</summary>
        public double SpeedAlarmThreshold { get; set; } = 3000.0;

        // =========== 通信参数 ===========

        /// <summary>TCP服务器IP地址</summary>
        public string TcpServerIp { get; set; } = "127.0.0.1";

        /// <summary>TCP服务器端口</summary>
        public int TcpServerPort { get; set; } = 9000;

        /// <summary>串口号</summary>
        public string SerialPortName { get; set; } = "COM1";

        /// <summary>串口波特率</summary>
        public int SerialBaudRate { get; set; } = 9600;

        // =========== 日志参数 ===========

        /// <summary>日志保存目录</summary>
        public string LogDirectory { get; set; } = "Logs";

        /// <summary>日志保留天数</summary>
        public int LogRetentionDays { get; set; } = 30;

        /// <summary>最大日志条数（内存中保留）</summary>
        public int MaxLogEntries { get; set; } = 1000;

        // =========== 界面参数 ===========

        /// <summary>系统名称（显示在标题栏）</summary>
        public string SystemName { get; set; } = "智能制造上位机系统";

        /// <summary>图表历史数据点数量</summary>
        public int ChartHistoryPoints { get; set; } = 60;
    }
}
