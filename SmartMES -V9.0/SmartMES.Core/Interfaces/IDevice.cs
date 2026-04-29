namespace SmartMES.Core.Interfaces
{
    /// <summary>
    /// 璁惧鎺ュ彛
    /// 闈㈠悜鎺ュ彛缂栫▼锛氭墍鏈夎澶囷紙PLC銆佺浉鏈恒€佷紶鎰熷櫒绛夛級閮藉疄鐜版鎺ュ彛
    /// 杩欐牱涓婂眰浠ｇ爜鍙緷璧栨帴鍙ｏ紝涓嶄緷璧栧叿浣撳疄鐜帮紝渚夸簬鎵╁睍鍜屾祴璇?
    /// </summary>
    public interface IDevice
    {
        /// <summary>璁惧鍞竴鏍囪瘑绗?/summary>
        string Id { get; }

        /// <summary>璁惧鍚嶇О锛堟樉绀虹敤锛?/summary>
        string Name { get; }

        /// <summary>璁惧绫诲瀷鎻忚堪</summary>
        string DeviceType { get; }

        /// <summary>鏄惁宸茶繛鎺?/summary>
        bool IsConnected { get; }

        /// <summary>璁惧鐘舵€佹弿杩?/summary>
        string Status { get; }

        /// <summary>鏈€鍚庝竴娆¤鍙栫殑鏁版嵁鍊?/summary>
        double LastValue { get; }

        /// <summary>
        /// 杩炴帴璁惧
        /// </summary>
        Task ConnectAsync();

        /// <summary>
        /// 鏂紑璁惧杩炴帴
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// 璇诲彇璁惧鏁版嵁
        /// </summary>
        Task<double> ReadDataAsync();

        /// <summary>
        /// 璁惧鐘舵€佸彉鍖栦簨浠?
        /// 鐢ㄤ簬閫氱煡涓婂眰鐘舵€佹敼鍙橈紙杩炴帴/鏂紑/閿欒绛夛級
        /// </summary>
        event EventHandler<DeviceStatusChangedEventArgs>? StatusChanged;
    }

    /// <summary>
    /// 璁惧鐘舵€佸彉鍖栦簨浠跺弬鏁?
    /// </summary>
    public class DeviceStatusChangedEventArgs : EventArgs
    {
        /// <summary>鏂扮姸鎬佹弿杩?/summary>
        public string NewStatus { get; }
        /// <summary>鏄惁宸茶繛鎺?/summary>
        public bool IsConnected { get; }

        /// <summary>
        /// 自动补齐：DeviceStatusChangedEventArgs 方法说明。
        /// </summary>
        public DeviceStatusChangedEventArgs(string newStatus, bool isConnected)
        {
            NewStatus = newStatus;
            IsConnected = isConnected;
        }
    }
}
