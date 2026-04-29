using SmartMES.Core.Infrastructure;
using SmartMES.Modules.MesComm;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartMES.UI.Modules.MesCommModule
{
    public class MesCommViewModel : ViewModelBase
    {
        private IMesClient? _client;
        private string _selectedProtocol = "HTTP REST";
        private bool _isConnected;
        private string _statusText = "未连接";

        public ObservableCollection<string> Protocols { get; } = new() { "HTTP REST", "MQTT", "OPC-UA" };

        public string SelectedProtocol
        {
            get => _selectedProtocol;
            set => SetProperty(ref _selectedProtocol, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (!SetProperty(ref _isConnected, value)) return;
                RefreshCommandState();
            }
        }

        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        public ObservableCollection<string> Messages { get; } = new();
        public ObservableCollection<MesWorkOrder> WorkOrders { get; } = new();

        public RelayCommand ConnectCommand { get; }
        public RelayCommand DisconnectCommand { get; }
        public RelayCommand GetOrdersCommand { get; }
        public RelayCommand ReportCommand { get; }
        public RelayCommand ClearCommand { get; }

        public MesCommViewModel()
        {
            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !_isConnected);
            DisconnectCommand = new RelayCommand(async _ => await DisconnectAsync(), _ => _isConnected);
            GetOrdersCommand = new RelayCommand(async _ => await GetOrdersAsync(), _ => _isConnected);
            ReportCommand = new RelayCommand(async _ => await ReportAsync(), _ => _isConnected);
            ClearCommand = new RelayCommand(_ => Messages.Clear());
        }

        private IMesClient CreateClient() => _selectedProtocol switch
        {
            "MQTT" => new MqttMesClient(),
            "OPC-UA" => new OpcUaMesClient(),
            _ => new HttpMesClient()
        };

        private async Task ConnectAsync()
        {
            _client = CreateClient();
            _client.MessageReceived += (_, msg) => AddMsg(msg);
            _client.Subscribe((tag, val) => AddMsg($"[DataPush] {tag} = {val}"));
            await _client.ConnectAsync();

            IsConnected = true;
            StatusText = $"{_selectedProtocol} 已连接";
            AddMsg("连接成功");
        }

        private async Task DisconnectAsync()
        {
            if (_client != null) await _client.DisconnectAsync();
            IsConnected = false;
            StatusText = "已断开";
            AddMsg("连接已断开");
        }

        private async Task GetOrdersAsync()
        {
            if (_client == null) return;
            var orders = await _client.GetWorkOrdersAsync();

            Application.Current?.Dispatcher.Invoke(() =>
            {
                WorkOrders.Clear();
                foreach (var o in orders) WorkOrders.Add(o);
            });

            AddMsg($"获取到 {orders.Count} 条工单");
        }

        private async Task ReportAsync()
        {
            if (_client == null) return;

            var result = new MesReportResult
            {
                OrderId = WorkOrders.FirstOrDefault()?.OrderId ?? "TEST",
                Qty = 10,
                IsPass = true
            };

            var ok = await _client.ReportResultAsync(result);
            AddMsg($"上报结果: {(ok ? "成功" : "失败")}");
        }

        private void RefreshCommandState()
        {
            ConnectCommand.RaiseCanExecuteChanged();
            DisconnectCommand.RaiseCanExecuteChanged();
            GetOrdersCommand.RaiseCanExecuteChanged();
            ReportCommand.RaiseCanExecuteChanged();
        }

        private void AddMsg(string msg)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Messages.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
                if (Messages.Count > 200) Messages.RemoveAt(Messages.Count - 1);
            });
        }
    }
}
