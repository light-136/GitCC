using SmartMES.Core.Infrastructure;
using SmartMES.Core.Models;
using SmartMES.Services.SecsGem;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace SmartMES.UI.Modules.SecsGemModule
{
    /// <summary>
    /// SECS/GEM 半导体通信监控 ViewModel。
    /// 设计职责：
    /// 1) 演示HSMS连接/断开生命周期；
    /// 2) 模拟E30 GEM双层状态机（通信状态 + 控制状态）变迁；
    /// 3) 维护SV/EC/CE等可观测数据并展示在数据网格中；
    /// 4) 模拟工艺程序(PP)管理；
    /// 5) 输出SECS消息发送/接收日志（仿真模式）。
    /// </summary>
    public class SecsGemViewModel : ViewModelBase
    {
        // GEM 服务核心实例（仿真模式：不会真正建立TCP连接）
        private readonly SecsGemService _service;
        // UI状态周期刷新定时器
        private readonly DispatcherTimer _refreshTimer;

        private string _connectionStatus = "未连接";
        private string _commState = "Disabled";
        private string _controlState = "EquipmentOffline";
        private string _hostAddress = "127.0.0.1";
        private int _hostPort = 5000;
        private bool _isConnected;
        private string _statusText = "就绪";
        private int _messageCount;

        public ObservableCollection<string> Logs { get; } = new();
        public ObservableCollection<SvDisplayItem> StatusVariables { get; } = new();
        public ObservableCollection<EcDisplayItem> EquipmentConstants { get; } = new();
        public ObservableCollection<string> ProcessPrograms { get; } = new();
        public ObservableCollection<string> AlarmList { get; } = new();

        /// <summary>连接状态文本（顶部卡片显示）。</summary>
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        /// <summary>GEM 通信状态（Disabled/WaitCRA/Communicating）。</summary>
        public string CommState
        {
            get => _commState;
            set => SetProperty(ref _commState, value);
        }

        /// <summary>GEM 控制状态（EquipmentOffline/AttemptOnline/HostOffline/OnLineLocal/OnLineRemote）。</summary>
        public string ControlState
        {
            get => _controlState;
            set => SetProperty(ref _controlState, value);
        }

        /// <summary>主机地址（用户可编辑）。</summary>
        public string HostAddress
        {
            get => _hostAddress;
            set => SetProperty(ref _hostAddress, value);
        }

        /// <summary>主机端口。</summary>
        public int HostPort
        {
            get => _hostPort;
            set => SetProperty(ref _hostPort, value);
        }

        /// <summary>是否已连接。</summary>
        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        /// <summary>状态栏文本。</summary>
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        /// <summary>累计SECS消息计数。</summary>
        public int MessageCount
        {
            get => _messageCount;
            set => SetProperty(ref _messageCount, value);
        }

        public RelayCommand ConnectCommand { get; }
        public RelayCommand DisconnectCommand { get; }
        public RelayCommand EnableCommand { get; }
        public RelayCommand GoOnlineCommand { get; }
        public RelayCommand GoOfflineCommand { get; }
        public RelayCommand SendEventCommand { get; }
        public RelayCommand ClearLogCommand { get; }
        public RelayCommand RefreshDataCommand { get; }

        /// <summary>创建 SECS/GEM 监控 ViewModel，初始化模拟服务、命令绑定及数据。</summary>
        public SecsGemViewModel()
        {
            // 注意：这里仅创建 SecsGemService 实例（仿真模式），不会真正发起 TCP 连接
            _service = new SecsGemService("127.0.0.1", 5000);

            RegisterDemoData();

            ConnectCommand     = new RelayCommand(async _ => await ConnectAsync());
            DisconnectCommand  = new RelayCommand(_ => Disconnect());
            EnableCommand      = new RelayCommand(async _ => await EnableAsync());
            GoOnlineCommand    = new RelayCommand(async _ => await GoOnlineAsync());
            GoOfflineCommand   = new RelayCommand(_ => GoOffline());
            SendEventCommand   = new RelayCommand(_ => SendDemoEvent());
            ClearLogCommand    = new RelayCommand(_ => Logs.Clear());
            RefreshDataCommand = new RelayCommand(_ => RefreshData());

            // 周期性刷新状态机状态（每2秒）
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += (_, __) => RefreshStates();
            _refreshTimer.Start();

            Log("SECS/GEM 模块初始化完成（仿真模式）");
            Log($"子模块：HSMS传输层 / SECS-II编解码 / GEM双层状态机");
            Log($"子模块：SV/EC/CE管理 / 告警管理 / 远程命令 / 工艺程序");
        }

        /// <summary>注册演示用 SV/EC/CE 数据，便于UI预览。</summary>
        private void RegisterDemoData()
        {
            // 状态变量(SV) — 设备运行时实时数据，主机可通过 S1F3 查询
            _service.RegisterStatusVariable(new StatusVariable { Id = 1001, Name = "温度", Units = "°C" });
            _service.RegisterStatusVariable(new StatusVariable { Id = 1002, Name = "压力", Units = "kPa" });
            _service.RegisterStatusVariable(new StatusVariable { Id = 1003, Name = "加工计数", Units = "pcs" });
            _service.RegisterStatusVariable(new StatusVariable { Id = 1004, Name = "良品率", Units = "%" });

            // 设备常量(EC) — 主机可通过 S2F13/F15 读写的工艺参数
            _service.RegisterEquipmentConstant(new EquipmentConstant { Id = 2001, Name = "温度上限", Units = "°C", Value = 85.0, MinValue = 0, MaxValue = 200 });
            _service.RegisterEquipmentConstant(new EquipmentConstant { Id = 2002, Name = "压力下限", Units = "kPa", Value = 50.0, MinValue = 0, MaxValue = 500 });
            _service.RegisterEquipmentConstant(new EquipmentConstant { Id = 2003, Name = "加工速率", Units = "mm/s", Value = 120.0, MinValue = 0, MaxValue = 1000 });

            // 采集事件(CE) — 设备主动推送给主机的事件
            // 注意：RegisterCollectionEvent 实际在 DataManager 上（SecsGemService 没有直接代理）
            _service.DataManager.RegisterCollectionEvent(new CollectionEvent { Id = 3001, Name = "批次开始", IsEnabled = true });
            _service.DataManager.RegisterCollectionEvent(new CollectionEvent { Id = 3002, Name = "批次完成", IsEnabled = true });
            _service.DataManager.RegisterCollectionEvent(new CollectionEvent { Id = 3003, Name = "工艺变更", IsEnabled = true });

            // 工艺程序(PP) — 用于S7族消息演示
            _service.ProcessProgramManager.StoreProgram("Recipe_A001", new byte[] { 0x01, 0x02, 0x03 });
            _service.ProcessProgramManager.StoreProgram("Recipe_B002", new byte[] { 0x04, 0x05, 0x06 });

            RefreshData();
        }

        /// <summary>模拟连接到 GEM 主机（仿真模式仅做UI状态切换）。</summary>
        private async Task ConnectAsync()
        {
            Log($"正在连接到 {HostAddress}:{HostPort} ...");
            StatusText = "连接中...";

            await Task.Delay(800);

            IsConnected = true;
            ConnectionStatus = $"已连接 ({HostAddress}:{HostPort})";
            StatusText = "已连接（仿真模式 - 无需真实TCP）";
            Log("HSMS 连接建立成功（仿真模式）");
            Log("发送 Select.req → 收到 Select.rsp (SType=2)");
            MessageCount += 2;
        }

        /// <summary>断开连接并复位状态机。</summary>
        private void Disconnect()
        {
            IsConnected = false;
            ConnectionStatus = "未连接";
            CommState = "Disabled";
            ControlState = "EquipmentOffline";
            StatusText = "已断开";
            Log("HSMS 连接已断开");
        }

        /// <summary>启用通信（模拟 S1F13/F14 交换）。</summary>
        private async Task EnableAsync()
        {
            if (!IsConnected) { Log("错误：请先连接到主机"); return; }

            // 触发GEM通信状态机：Disabled → WaitCRA → Communicating
            _service.StateMachine.Enable();
            await Task.Delay(300);

            _service.StateMachine.CommunicationEstablished();
            CommState = _service.StateMachine.CommunicationState.ToString();

            Log("→ S1F13 (Establish Communication Request)");
            Log("← S1F14 (Establish Communication Acknowledge, COMMACK=0)");
            MessageCount += 2;
            StatusText = "通信已建立";
        }

        /// <summary>请求上线（模拟 S1F17/F18 交换）。</summary>
        private async Task GoOnlineAsync()
        {
            if (CommState != "Communicating") { Log("错误：请先建立通信"); return; }

            _service.StateMachine.RequestOnline();
            await Task.Delay(200);
            // OnlineAccepted: 主机批准上线（对应 ONLACK=0）
            _service.StateMachine.OnlineAccepted();
            ControlState = _service.StateMachine.ControlState.ToString();

            Log("→ S1F17 (Request Online)");
            Log("← S1F18 (Online Acknowledge, ONLACK=0 — 批准上线)");
            MessageCount += 2;
            StatusText = "在线运行中";
        }

        /// <summary>请求离线（模拟 S1F15/F16）。</summary>
        private void GoOffline()
        {
            _service.StateMachine.GoOffline();
            ControlState = _service.StateMachine.ControlState.ToString();
            Log("← S1F15 (Offline Request)");
            Log("→ S1F16 (Offline Acknowledge)");
            MessageCount += 2;
            StatusText = "设备离线";
        }

        /// <summary>发送演示事件报告（S6F11/F12），随机更新SV并触发CE。</summary>
        private void SendDemoEvent()
        {
            var rnd = Random.Shared;
            var ceid = 3001 + rnd.Next(0, 3);
            var eventName = ceid switch { 3001 => "批次开始", 3002 => "批次完成", _ => "工艺变更" };

            Log($"→ S6F11 (Event Report: CEID={ceid}, Name={eventName})");
            Log($"← S6F12 (Event Report Acknowledge, ACKC6=0)");
            MessageCount += 2;

            // SetStatusVariableValue 实际位于 DataManager 上，SecsGemService 没有直接代理
            _service.DataManager.SetStatusVariableValue(1001, 25.0 + rnd.NextDouble() * 60);
            _service.DataManager.SetStatusVariableValue(1002, 80.0 + rnd.NextDouble() * 40);
            _service.DataManager.SetStatusVariableValue(1003, rnd.Next(100, 9999));
            _service.DataManager.SetStatusVariableValue(1004, 85.0 + rnd.NextDouble() * 15);

            RefreshData();
        }

        /// <summary>从GemDataManager刷新所有数据展示。</summary>
        private void RefreshData()
        {
            StatusVariables.Clear();
            foreach (var sv in _service.DataManager.GetAllStatusVariables())
            {
                StatusVariables.Add(new SvDisplayItem
                {
                    Id = sv.Id,
                    Name = sv.Name,
                    Value = sv.Value?.ToString() ?? "N/A",
                    Units = sv.Units
                });
            }

            EquipmentConstants.Clear();
            foreach (var ec in _service.DataManager.GetAllEquipmentConstants())
            {
                EquipmentConstants.Add(new EcDisplayItem
                {
                    Id = ec.Id,
                    Name = ec.Name,
                    Value = ec.Value?.ToString() ?? "",
                    Units = ec.Units
                });
            }

            ProcessPrograms.Clear();
            foreach (var pp in _service.ProcessProgramManager.GetProgramList())
                ProcessPrograms.Add(pp);
        }

        /// <summary>定时刷新双层状态机状态显示。</summary>
        private void RefreshStates()
        {
            CommState = _service.StateMachine.CommunicationState.ToString();
            ControlState = _service.StateMachine.ControlState.ToString();
        }

        /// <summary>追加日志（带时间戳，自动裁剪超过500条的旧日志）。</summary>
        private void Log(string msg)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            if (Logs.Count > 500) Logs.RemoveAt(0);
            Logs.Add(entry);
        }
    }

    /// <summary>状态变量显示项（用于DataGrid绑定）。</summary>
    public class SvDisplayItem
    {
        public uint Id { get; set; }
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Units { get; set; } = "";
    }

    /// <summary>设备常量显示项（用于DataGrid绑定）。</summary>
    public class EcDisplayItem
    {
        public uint Id { get; set; }
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Units { get; set; } = "";
    }
}
