// ============================================================
// 文件：SecsGemViewModel.cs
// 用途：SECS/GEM通讯监控页面ViewModel
// 设计思路：
//   SECS/GEM是半导体行业标准通讯协议。
//   此页面用于：
//   1. 连接管理 — 配置Host IP/Port，建立HSMS连接
//   2. 消息收发 — 发送标准SECS消息，查看响应
//   3. GEM状态 — 显示当前控制状态和通讯状态
//   4. 消息日志 — 记录所有收发消息
// ============================================================

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;

namespace SmartSemiCon.UI.ViewModels
{
    /// <summary>
    /// SECS/GEM监控页面ViewModel。
    /// </summary>
    public partial class SecsGemViewModel : ObservableObject
    {
        private readonly ISecsGemService _secsGemService;
        private readonly ILogService _logService;

        [ObservableProperty]
        private string _hostIp = "127.0.0.1";

        [ObservableProperty]
        private int _hostPort = 5000;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _connectionState = "未连接";

        [ObservableProperty]
        private string _controlState = "离线";

        [ObservableProperty]
        private string _communicationState = "未通讯";

        [ObservableProperty]
        private int _messageCount;

        /// <summary>消息日志</summary>
        public ObservableCollection<SecsMessageLogEntry> MessageLog { get; } = new();

        public SecsGemViewModel(ISecsGemService secsGemService, ILogService logService)
        {
            _secsGemService = secsGemService;
            _logService = logService;

            _secsGemService.MessageReceived += OnMessageReceived;
        }

        /// <summary>连接到Host</summary>
        [RelayCommand]
        private async Task Connect()
        {
            await _secsGemService.ConnectAsync(HostIp, HostPort);
            IsConnected = true;
            ConnectionState = "已连接";
            ControlState = "在线-本地";
            CommunicationState = "通讯中";
            AddMessage("OUT", "HSMS", "Select.req → 建立HSMS连接");
            _logService.Log(LogLevel.Info, "SECS/GEM", $"连接到 {HostIp}:{HostPort}");
        }

        /// <summary>断开连接</summary>
        [RelayCommand]
        private async Task Disconnect()
        {
            await _secsGemService.DisconnectAsync();
            IsConnected = false;
            ConnectionState = "未连接";
            ControlState = "离线";
            CommunicationState = "未通讯";
            AddMessage("OUT", "HSMS", "Separate.req → 断开HSMS连接");
            _logService.Log(LogLevel.Info, "SECS/GEM", "已断开连接");
        }

        /// <summary>发送S1F1（Are You There）</summary>
        [RelayCommand]
        private async Task SendS1F1()
        {
            var reply = await _secsGemService.SendAsync(1, 1, Array.Empty<byte>());
            AddMessage("OUT", "S1F1", "Are You There?");
            if (reply != null)
                AddMessage("IN", "S1F2", $"On Line Data: SystemBytes={reply.SystemBytes}");
        }

        /// <summary>发送S1F13（Establish Communication）</summary>
        [RelayCommand]
        private async Task SendS1F13()
        {
            var reply = await _secsGemService.SendAsync(1, 13, Array.Empty<byte>());
            AddMessage("OUT", "S1F13", "Establish Communication Request");
            if (reply != null)
                AddMessage("IN", "S1F14", $"通讯建立确认 Body={reply.Body.Length}bytes");
        }

        /// <summary>发送S2F41（Host Command）</summary>
        [RelayCommand]
        private async Task SendS2F41()
        {
            var reply = await _secsGemService.SendAsync(2, 41, Array.Empty<byte>());
            AddMessage("OUT", "S2F41", "Host Command Send: START");
            if (reply != null)
                AddMessage("IN", "S2F42", "Host Command Acknowledge");
        }

        private void OnMessageReceived(object? sender, SecsMessageData msg)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                AddMessage("IN", $"S{msg.Stream}F{msg.Function}", $"设备回复 Bytes={msg.Body.Length}");
            });
        }

        private void AddMessage(string direction, string msgId, string detail)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                MessageCount++;
                MessageLog.Insert(0, new SecsMessageLogEntry
                {
                    Time = DateTime.Now,
                    Direction = direction,
                    MessageId = msgId,
                    Detail = detail
                });
                if (MessageLog.Count > 500) MessageLog.RemoveAt(MessageLog.Count - 1);
            });
        }
    }

    /// <summary>SECS消息日志条目</summary>
    public class SecsMessageLogEntry
    {
        public DateTime Time { get; set; }
        public string Direction { get; set; } = "";
        public string MessageId { get; set; } = "";
        public string Detail { get; set; } = "";
    }
}
