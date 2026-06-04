// 主视图模型
// 连接WPF界面与所有业务逻辑，采用CommunityToolkit.Mvvm简化MVVM开发
//
// 设计思路：
// 1. 串口配置区 → 绑定到串口参数属性
// 2. 固件选择区 → 绑定到文件路径和信息属性
// 3. 操作控制区 → 绑定到各操作命令
// 4. 进度显示区 → 绑定到进度和状态属性
// 5. 日志区 → 绑定到日志集合

using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GD32FirmwareUpdater.Models;
using GD32FirmwareUpdater.Services;
using Microsoft.Win32;

namespace GD32FirmwareUpdater.ViewModels
{
    /// <summary>
    /// 主界面视图模型
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        private readonly SerialPortService _serialPortService;
        private FirmwareUpgradeService? _upgradeService;
        private CancellationTokenSource? _upgradeCts;
        private FirmwareInfo? _firmwareInfo;

        // ========== 串口配置相关属性 ==========

        // 可用COM端口列表
        [ObservableProperty]
        private ObservableCollection<string> _availablePorts = new();

        // 当前选中的COM端口
        [ObservableProperty]
        private string _selectedPort = string.Empty;

        // 波特率选项列表
        public int[] BaudRateOptions { get; } = { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };

        // 当前选中的波特率
        [ObservableProperty]
        private int _selectedBaudRate = 115200;

        // 数据位选项
        public int[] DataBitsOptions { get; } = { 7, 8 };

        // 当前选中的数据位
        [ObservableProperty]
        private int _selectedDataBits = 8;

        // 停止位选项
        public StopBits[] StopBitsOptions { get; } = { StopBits.One, StopBits.Two };

        // 当前选中的停止位
        [ObservableProperty]
        private StopBits _selectedStopBits = StopBits.One;

        // 校验位选项
        public Parity[] ParityOptions { get; } = { Parity.None, Parity.Odd, Parity.Even };

        // 当前选中的校验位
        [ObservableProperty]
        private Parity _selectedParity = Parity.Even;

        // 串口是否已连接
        [ObservableProperty]
        private bool _isConnected;

        // 串口连接/断开按钮文本
        [ObservableProperty]
        private string _connectButtonText = "打开串口";

        // ========== 固件文件相关属性 ==========

        // 固件文件路径
        [ObservableProperty]
        private string _firmwareFilePath = string.Empty;

        // 固件文件大小描述
        [ObservableProperty]
        private string _firmwareFileSize = string.Empty;

        // ========== 升级状态相关属性 ==========

        // 升级进度（0-100）
        [ObservableProperty]
        private int _upgradeProgress;

        // 升级状态消息
        [ObservableProperty]
        private string _statusMessage = "就绪";

        // 是否正在升级中
        [ObservableProperty]
        private bool _isUpgrading;

        // Bootloader版本号
        [ObservableProperty]
        private string _bootloaderVersion = "未知";

        // ========== 日志 ==========

        // 日志内容集合
        [ObservableProperty]
        private ObservableCollection<string> _logMessages = new();

        // 日志滚动触发（每次新增日志+1，用于触发ScrollViewer自动滚动）
        [ObservableProperty]
        private int _logScrollTrigger;

        public MainViewModel()
        {
            _serialPortService = new SerialPortService();

            // 订阅串口日志和连接状态事件
            _serialPortService.OnLog += msg => AddLog(msg);
            _serialPortService.OnConnectionChanged += connected =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsConnected = connected;
                    ConnectButtonText = connected ? "关闭串口" : "打开串口";
                });
            };

            // 初始化时刷新串口列表
            RefreshPorts();
        }

        // ========== 命令实现 ==========

        /// <summary>
        /// 刷新可用串口列表
        /// </summary>
        [RelayCommand]
        private void RefreshPorts()
        {
            AvailablePorts.Clear();
            var ports = SerialPortService.GetAvailablePorts();
            foreach (var port in ports.OrderBy(p => p))
            {
                AvailablePorts.Add(port);
            }

            if (AvailablePorts.Count > 0 && string.IsNullOrEmpty(SelectedPort))
            {
                SelectedPort = AvailablePorts[0];
            }

            AddLog($"检测到 {ports.Length} 个可用串口");
        }

        /// <summary>
        /// 切换串口连接状态（打开/关闭）
        /// </summary>
        [RelayCommand]
        private void ToggleConnect()
        {
            if (IsConnected)
            {
                _serialPortService.Close();
            }
            else
            {
                if (string.IsNullOrEmpty(SelectedPort))
                {
                    AddLog("请先选择COM端口");
                    return;
                }

                var config = new SerialPortConfig
                {
                    PortName = SelectedPort,
                    BaudRate = SelectedBaudRate,
                    DataBits = SelectedDataBits,
                    StopBits = SelectedStopBits,
                    Parity = SelectedParity
                };

                _serialPortService.Open(config);
            }
        }

        /// <summary>
        /// 选择固件文件
        /// </summary>
        [RelayCommand]
        private void SelectFirmware()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择固件文件",
                Filter = "BIN固件文件 (*.bin)|*.bin|所有文件 (*.*)|*.*",
                FilterIndex = 1
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    _firmwareInfo = FirmwareFileParser.LoadFirmware(dialog.FileName);
                    FirmwareFilePath = _firmwareInfo.FilePath;
                    FirmwareFileSize = _firmwareInfo.FileSizeText;
                    AddLog($"已加载固件: {_firmwareInfo.FileName} ({_firmwareInfo.FileSizeText})");
                }
                catch (Exception ex)
                {
                    AddLog($"加载固件失败: {ex.Message}");
                    _firmwareInfo = null;
                    FirmwareFilePath = string.Empty;
                    FirmwareFileSize = string.Empty;
                }
            }
        }

        /// <summary>
        /// 开始固件升级
        /// </summary>
        [RelayCommand]
        private async Task StartUpgradeAsync()
        {
            // 前置校验
            if (!IsConnected)
            {
                AddLog("请先打开串口");
                return;
            }

            if (_firmwareInfo == null || _firmwareInfo.Data.Length == 0)
            {
                AddLog("请先选择固件文件");
                return;
            }

            if (IsUpgrading)
            {
                AddLog("升级正在进行中...");
                return;
            }

            IsUpgrading = true;
            UpgradeProgress = 0;
            StatusMessage = "开始升级...";

            _upgradeService = new FirmwareUpgradeService(_serialPortService);
            _upgradeService.OnProgress += progress =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpgradeProgress = progress.Percentage;
                    StatusMessage = progress.Message;
                });
            };
            _upgradeService.OnLog += msg => AddLog(msg);

            _upgradeCts = new CancellationTokenSource();

            try
            {
                bool success = await _upgradeService.ExecuteUpgradeAsync(_firmwareInfo.Data, _upgradeCts.Token);

                if (success)
                {
                    StatusMessage = "固件升级成功！";
                    AddLog("========== 固件升级成功 ==========");
                }
                else
                {
                    StatusMessage = "固件升级失败";
                    AddLog("========== 固件升级失败 ==========");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"升级异常: {ex.Message}";
                AddLog($"升级异常: {ex.Message}");
            }
            finally
            {
                IsUpgrading = false;
                _upgradeCts?.Dispose();
                _upgradeCts = null;
            }
        }

        /// <summary>
        /// 取消升级操作
        /// </summary>
        [RelayCommand]
        private void CancelUpgrade()
        {
            if (_upgradeCts != null && !_upgradeCts.IsCancellationRequested)
            {
                _upgradeCts.Cancel();
                AddLog("正在取消升级...");
            }
        }

        /// <summary>
        /// 查询Bootloader版本号
        /// </summary>
        [RelayCommand]
        private async Task QueryVersionAsync()
        {
            if (!IsConnected)
            {
                AddLog("请先打开串口");
                return;
            }

            _upgradeService = new FirmwareUpgradeService(_serialPortService);
            _upgradeService.OnLog += msg => AddLog(msg);

            var version = await _upgradeService.QueryVersionAsync();
            if (version != null)
            {
                BootloaderVersion = version;
            }
            else
            {
                BootloaderVersion = "查询失败";
            }
        }

        /// <summary>
        /// 清空日志
        /// </summary>
        [RelayCommand]
        private void ClearLog()
        {
            LogMessages.Clear();
        }

        /// <summary>
        /// 添加日志（线程安全）
        /// </summary>
        private void AddLog(string message)
        {
            if (Application.Current?.Dispatcher == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                LogMessages.Add(message);
                // 限制日志条数，防止内存溢出
                while (LogMessages.Count > 5000)
                {
                    LogMessages.RemoveAt(0);
                }
                LogScrollTrigger++;
            });
        }
    }
}
