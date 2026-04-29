using SmartMES.Core.Infrastructure;
using SmartMES.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartMES.UI.ViewModels
{
    /// <summary>
    /// 閫氫俊鐩戞帶ViewModel
    /// 绠＄悊TCP鍜屼覆鍙ｉ€氫俊浼氳瘽锛屾樉绀烘敹鍙戞棩蹇?
    /// </summary>
    public class CommunicationViewModel : ViewModelBase
    {
        private readonly ICommunicationService _tcpService;
        private readonly ICommunicationService _serialService;
        private readonly ILoggingService _logger;

        public ObservableCollection<string> CommLogs { get; } = new();

        private bool _tcpConnected;
        public bool TcpConnected
        {
            get => _tcpConnected;
            set => SetProperty(ref _tcpConnected, value);
        }

        private bool _serialConnected;
        public bool SerialConnected
        {
            get => _serialConnected;
            set => SetProperty(ref _serialConnected, value);
        }

        private string _sendText = "01 03 00 00 00 01";
        public string SendText
        {
            get => _sendText;
            set => SetProperty(ref _sendText, value);
        }

        public RelayCommand TcpConnectCommand { get; }
        public RelayCommand TcpDisconnectCommand { get; }
        public RelayCommand SerialConnectCommand { get; }
        public RelayCommand SerialDisconnectCommand { get; }
        public RelayCommand SendTcpCommand { get; }
        public RelayCommand SendSerialCommand { get; }
        public RelayCommand ReceiveTcpCommand { get; }
        public RelayCommand ClearLogCommand { get; }

        /// <summary>
        /// 自动补齐：CommunicationViewModel 方法说明。
        /// </summary>
        public CommunicationViewModel(
            ICommunicationService tcpService,
            ICommunicationService serialService,
            ILoggingService logger)
        {
            _tcpService = tcpService;
            _serialService = serialService;
            _logger = logger;

            // 璁㈤槄鏁版嵁鎺ユ敹浜嬩欢
            _tcpService.DataReceived += (_, d) => AddLog($"[TCP-RX] {BitConverter.ToString(d)}");
            _serialService.DataReceived += (_, d) => AddLog($"[SRL-RX] {BitConverter.ToString(d)}");
            _tcpService.ConnectionChanged += (_, c) => Application.Current?.Dispatcher.Invoke(() => TcpConnected = c);
            _serialService.ConnectionChanged += (_, c) => Application.Current?.Dispatcher.Invoke(() => SerialConnected = c);

            TcpConnectCommand = new RelayCommand(async _ => await _tcpService.ConnectAsync(), _ => !_tcpConnected);
            TcpDisconnectCommand = new RelayCommand(async _ => await _tcpService.DisconnectAsync(), _ => _tcpConnected);
            SerialConnectCommand = new RelayCommand(async _ => await _serialService.ConnectAsync(), _ => !_serialConnected);
            SerialDisconnectCommand = new RelayCommand(async _ => await _serialService.DisconnectAsync(), _ => _serialConnected);

            SendTcpCommand = new RelayCommand(async _ =>
            {
                try
                {
                    var bytes = ParseHex(_sendText);
                    await _tcpService.SendAsync(bytes);
                    AddLog($"[TCP-TX] {BitConverter.ToString(bytes)}");
                }
                catch (Exception ex) { AddLog($"[TCP-ERR] {ex.Message}"); }
            }, _ => _tcpConnected);

            SendSerialCommand = new RelayCommand(async _ =>
            {
                try
                {
                    var bytes = ParseHex(_sendText);
                    await _serialService.SendAsync(bytes);
                    AddLog($"[SRL-TX] {BitConverter.ToString(bytes)}");
                }
                catch (Exception ex) { AddLog($"[SRL-ERR] {ex.Message}"); }
            }, _ => _serialConnected);

            ReceiveTcpCommand = new RelayCommand(async _ => await _tcpService.ReceiveAsync(), _ => _tcpConnected);
            ClearLogCommand = new RelayCommand(_ => CommLogs.Clear());
        }

        /// <summary>
        /// 自动补齐：AddLog 方法说明。
        /// </summary>
        private void AddLog(string msg)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                CommLogs.Add($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
                if (CommLogs.Count > 200) CommLogs.RemoveAt(0);
            });
        }

        /// <summary>灏嗗崄鍏繘鍒跺瓧绗︿覆瑙ｆ瀽涓哄瓧鑺傛暟缁勶紙濡?"01 03 00"锛?/summary>
        private static byte[] ParseHex(string hex)
        {
            var parts = hex.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(p => Convert.ToByte(p, 16)).ToArray();
        }
    }
}
