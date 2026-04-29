namespace SmartMES.Core.Models
{
    /// <summary>
    /// 设备数据模型
    /// 纯数据类，不包含任何业务逻辑
    /// Model层：只负责描述数据结构
    /// </summary>
    public class DeviceModel
    {
        /// <summary>设备唯一ID</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>设备名称</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>设备类型（PLC / Camera / Sensor）</summary>
        public string DeviceType { get; set; } = string.Empty;

        /// <summary>设备地址（IP或串口号）</summary>
        public string Address { get; set; } = string.Empty;

        /// <summary>端口号</summary>
        public int Port { get; set; }

        /// <summary>是否已连接</summary>
        public bool IsConnected { get; set; }

        /// <summary>当前状态描述</summary>
        public string Status { get; set; } = "未连接";

        /// <summary>最后读取的数值</summary>
        public double LastValue { get; set; }

        /// <summary>最后更新时间</summary>
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        /// <summary>设备描述信息</summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 实时采样数据点
    /// 用于图表显示的时间序列数据
    /// </summary>
    public class DataPoint
    {
        /// <summary>采样时间</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>数值</summary>
        public double Value { get; set; }

        /// <summary>数据来源设备名</summary>
        public string DeviceName { get; set; } = string.Empty;
    }
}
