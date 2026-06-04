// 串口配置数据模型
// 存储串口通讯所需的各项参数

using System.IO.Ports;

namespace GD32FirmwareUpdater.Models
{
    /// <summary>
    /// 串口配置参数模型
    /// 默认值参照官方工具 ConfigInfo.ini：115200, 8, 1, Even
    /// </summary>
    public class SerialPortConfig
    {
        // COM端口名称（如 COM1, COM3）
        public string PortName { get; set; } = string.Empty;

        // 波特率（默认115200）
        public int BaudRate { get; set; } = 115200;

        // 数据位（默认8位）
        public int DataBits { get; set; } = 8;

        // 停止位（默认1位）
        public StopBits StopBits { get; set; } = StopBits.One;

        // 校验位（默认偶校验，与协议官方配置一致）
        public Parity Parity { get; set; } = Parity.Even;

        // 读取超时（毫秒）
        public int ReadTimeout { get; set; } = 3000;

        // 写入超时（毫秒）
        public int WriteTimeout { get; set; } = 3000;
    }
}
