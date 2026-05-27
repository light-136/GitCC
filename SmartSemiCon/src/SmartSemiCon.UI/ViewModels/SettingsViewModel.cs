// ============================================================
// 文件：SettingsViewModel.cs
// 用途：系统设置页面ViewModel
// 设计思路：
//   系统设置管理设备的全局参数：
//   1. 通讯设置 — TCP/SECS端口、超时
//   2. 运动设置 — 全局速度限制、安全参数
//   3. 视觉设置 — 默认曝光、增益
//   4. 系统设置 — 语言、主题、日志级别
// ============================================================

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;

namespace SmartSemiCon.UI.ViewModels
{
    /// <summary>
    /// 系统设置页面ViewModel。
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ILogService _logService;

        [ObservableProperty]
        private string _tcpServerIp = "0.0.0.0";

        [ObservableProperty]
        private int _tcpServerPort = 9000;

        [ObservableProperty]
        private int _heartbeatInterval = 5000;

        [ObservableProperty]
        private int _reconnectInterval = 3000;

        [ObservableProperty]
        private string _secsHostIp = "127.0.0.1";

        [ObservableProperty]
        private int _secsPort = 5000;

        [ObservableProperty]
        private int _secsDeviceId = 1;

        [ObservableProperty]
        private int _secsT3Timeout = 45;

        [ObservableProperty]
        private double _globalMaxSpeed = 500.0;

        [ObservableProperty]
        private double _globalMaxAccel = 1000.0;

        [ObservableProperty]
        private bool _enableSoftLimit = true;

        [ObservableProperty]
        private bool _enableInterlock = true;

        [ObservableProperty]
        private double _defaultExposure = 10.0;

        [ObservableProperty]
        private double _defaultGain = 1.0;

        [ObservableProperty]
        private string _imageFormat = "BMP";

        [ObservableProperty]
        private string _selectedLanguage = "中文";

        [ObservableProperty]
        private string _selectedTheme = "暗色";

        [ObservableProperty]
        private string _selectedLogLevel = "Info";

        [ObservableProperty]
        private int _logRetentionDays = 30;

        /// <summary>语言选项</summary>
        public ObservableCollection<string> Languages { get; } = new() { "中文", "English" };

        /// <summary>主题选项</summary>
        public ObservableCollection<string> Themes { get; } = new() { "暗色", "亮色" };

        /// <summary>日志级别选项</summary>
        public ObservableCollection<string> LogLevels { get; } = new() { "Debug", "Info", "Warning", "Error" };

        /// <summary>图像格式选项</summary>
        public ObservableCollection<string> ImageFormats { get; } = new() { "BMP", "PNG", "JPEG", "TIFF" };

        public SettingsViewModel(ILogService logService)
        {
            _logService = logService;
        }

        /// <summary>保存所有设置</summary>
        [RelayCommand]
        private void SaveSettings()
        {
            _logService.Log(LogLevel.Info, "系统设置", "设置已保存");
        }

        /// <summary>恢复默认设置</summary>
        [RelayCommand]
        private void RestoreDefaults()
        {
            TcpServerIp = "0.0.0.0";
            TcpServerPort = 9000;
            HeartbeatInterval = 5000;
            ReconnectInterval = 3000;
            SecsHostIp = "127.0.0.1";
            SecsPort = 5000;
            SecsDeviceId = 1;
            SecsT3Timeout = 45;
            GlobalMaxSpeed = 500.0;
            GlobalMaxAccel = 1000.0;
            EnableSoftLimit = true;
            EnableInterlock = true;
            DefaultExposure = 10.0;
            DefaultGain = 1.0;
            ImageFormat = "BMP";
            SelectedLanguage = "中文";
            SelectedTheme = "暗色";
            SelectedLogLevel = "Info";
            LogRetentionDays = 30;
            _logService.Log(LogLevel.Info, "系统设置", "已恢复默认设置");
        }
    }
}
