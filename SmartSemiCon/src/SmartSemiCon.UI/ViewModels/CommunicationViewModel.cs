// ============================================================
// 文件：CommunicationViewModel.cs
// 用途：TCP通讯监控页面ViewModel
// 设计思路：
//   使用ITcpServer和ICommunicationService接口操作通讯模块。
//   此页面提供：
//   1. 服务端启动/停止 — 监听指定端口
//   2. 客户端连接 — 连接到远程设备
//   3. 消息收发 — 发送文本消息，查看收到的数据
//   4. 连接状态 — 显示客户端连接数
// ============================================================

using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;

namespace SmartSemiCon.UI.ViewModels
{
    /// <summary>
    /// 通讯监控页面ViewModel。
    /// </summary>
    public partial class CommunicationViewModel : ObservableObject
    {
        private readonly ILogService _logService;
        private ITcpServer? _server;
        private ICommunicationService? _client;

        [ObservableProperty]
        private int _serverPort = 9000;

        [ObservableProperty]
        private bool _isServerRunning;

        [ObservableProperty]
        private string _clientHost = "127.0.0.1";

        [ObservableProperty]
        private int _clientPort = 9000;

        [ObservableProperty]
        private bool _isClientConnected;

        [ObservableProperty]
        private string _sendMessage = "";

        [ObservableProperty]
        private int _connectedClientCount;

        /// <summary>通讯日志</summary>
        public ObservableCollection<CommLogEntry> CommLogs { get; } = new();

        public CommunicationViewModel(ILogService logService)
        {
            _logService = logService;
        }

        /// <summary>启动TCP服务端</summary>
        [RelayCommand]
        private async Task StartServer()
        {
            _server = new Infrastructure.Communication.Core.AsyncTcpServer();
            _server.DataReceived += (_, args) =>
            {
                var text = Encoding.UTF8.GetString(args.Data);
                AddLog("SERVER-RX", $"来自{args.ClientId}: {text}");
            };
            _server.ClientConnected += (_, id) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() => ConnectedClientCount++);
                AddLog("SERVER", $"客户端连接: {id}");
            };
            _server.ClientDisconnected += (_, id) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() => ConnectedClientCount--);
                AddLog("SERVER", $"客户端断开: {id}");
            };
            await _server.StartAsync(ServerPort);
            IsServerRunning = true;
            AddLog("SERVER", $"服务端已启动，端口={ServerPort}");
        }

        /// <summary>停止TCP服务端</summary>
        [RelayCommand]
        private async Task StopServer()
        {
            if (_server != null)
            {
                await _server.StopAsync();
                (_server as IDisposable)?.Dispose();
                _server = null;
            }
            IsServerRunning = false;
            ConnectedClientCount = 0;
            AddLog("SERVER", "服务端已停止");
        }

        /// <summary>连接TCP客户端</summary>
        [RelayCommand]
        private async Task ConnectClient()
        {
            _client = new Infrastructure.Communication.Core.AsyncTcpClient("CommClient");
            _client.DataReceived += (_, data) =>
            {
                var text = Encoding.UTF8.GetString(data);
                AddLog("CLIENT-RX", text);
            };
            _client.StateChanged += (_, state) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsClientConnected = state == ConnectionState.Connected;
                    AddLog("CLIENT", $"状态: {state}");
                });
            };
            await _client.ConnectAsync(ClientHost, ClientPort);
            IsClientConnected = true;
            AddLog("CLIENT", $"连接到 {ClientHost}:{ClientPort}");
        }

        /// <summary>断开客户端</summary>
        [RelayCommand]
        private async Task DisconnectClient()
        {
            if (_client != null)
            {
                await _client.DisconnectAsync();
                _client.Dispose();
                _client = null;
            }
            IsClientConnected = false;
            AddLog("CLIENT", "客户端已断开");
        }

        /// <summary>发送消息</summary>
        [RelayCommand]
        private async Task Send()
        {
            if (string.IsNullOrWhiteSpace(SendMessage)) return;

            var data = Encoding.UTF8.GetBytes(SendMessage);
            if (_client != null && IsClientConnected)
            {
                await _client.SendAsync(data);
                AddLog("CLIENT-TX", SendMessage);
            }
            else if (_server != null && IsServerRunning)
            {
                await _server.BroadcastAsync(data);
                AddLog("SERVER-TX", $"广播: {SendMessage}");
            }
            SendMessage = "";
        }

        private void AddLog(string source, string message)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                CommLogs.Insert(0, new CommLogEntry
                {
                    Time = DateTime.Now,
                    Source = source,
                    Message = message
                });
                if (CommLogs.Count > 500) CommLogs.RemoveAt(CommLogs.Count - 1);
            });
        }
    }

    /// <summary>通讯日志条目</summary>
    public class CommLogEntry
    {
        public DateTime Time { get; set; }
        public string Source { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
