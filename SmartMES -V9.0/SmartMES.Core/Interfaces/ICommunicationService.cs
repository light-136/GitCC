namespace SmartMES.Core.Interfaces
{
    /// <summary>
    /// 通信服务接口
    /// 定义上位机通信的统一契约，支持TCP、串口等多种协议
    /// 面向接口设计使得可以在不修改上层代码的情况下切换通信方式
    /// </summary>
    public interface ICommunicationService
    {
        /// <summary>是否已连接</summary>
        bool IsConnected { get; }

        /// <summary>通信类型描述（如"TCP"、"SerialPort"）</summary>
        string ProtocolName { get; }

        /// <summary>
        /// 建立连接
        /// </summary>
        Task ConnectAsync();

        /// <summary>
        /// 断开连接
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="data">要发送的字节数据</param>
        Task SendAsync(byte[] data);

        /// <summary>
        /// 接收数据
        /// </summary>
        /// <returns>接收到的字节数据</returns>
        Task<byte[]> ReceiveAsync();

        /// <summary>数据接收事件（异步推送模式）</summary>
        event EventHandler<byte[]>? DataReceived;

        /// <summary>连接状态变化事件</summary>
        event EventHandler<bool>? ConnectionChanged;
    }
}
