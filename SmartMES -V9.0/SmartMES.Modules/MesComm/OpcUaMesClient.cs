namespace SmartMES.Modules.MesComm
{
    /// <summary>
    /// OPC-UA MES客户端实现（模拟）。
    /// </summary>
    public class OpcUaMesClient : IMesClient
    {
        private bool _isConnected;
        private readonly string _endpointUrl;
        private Action<string, object>? _dataHandler;
        private CancellationTokenSource? _cts;
        private readonly Random _rnd = new();

        public string ProtocolName => "OPC-UA";
        public bool IsConnected => _isConnected;
        public event EventHandler<string>? MessageReceived;

        private readonly Dictionary<string, double> _nodeValues = new()
        {
            ["ns=2;s=Line1.Temperature"] = 45.0,
            ["ns=2;s=Line1.Pressure"] = 6.5,
            ["ns=2;s=Line1.SpindleSpeed"] = 2500.0,
            ["ns=2;s=Line1.FeedRate"] = 1000.0,
        };

        /// <summary>构造 OPC-UA 客户端。</summary>
        public OpcUaMesClient(string endpointUrl = "opc.tcp://localhost:4840")
        {
            _endpointUrl = endpointUrl;
        }

        /// <summary>建立会话并开始节点模拟推送。</summary>
        public async Task ConnectAsync()
        {
            await Task.Delay(600);
            _isConnected = true;
            _cts = new CancellationTokenSource();
            MessageReceived?.Invoke(this, $"[OPC-UA] 已连接: {_endpointUrl}");
            MessageReceived?.Invoke(this, $"[OPC-UA] 订阅 {_nodeValues.Count} 个节点");
            _ = SimulateNodeChangeAsync(_cts.Token);
        }

        /// <summary>关闭会话并停止推送。</summary>
        public async Task DisconnectAsync()
        {
            _cts?.Cancel();
            await Task.Delay(100);
            _isConnected = false;
            MessageReceived?.Invoke(this, "[OPC-UA] 会话已关闭");
        }

        /// <summary>获取工单列表（模拟）。</summary>
        public async Task<List<MesWorkOrder>> GetWorkOrdersAsync()
        {
            await Task.Delay(200);
            return new List<MesWorkOrder>
            {
                new() { OrderId="WO-OPC-001", ProductCode="OP01", ProductName="OPC工单A", PlannedQty=120, Status="Running" },
                new() { OrderId="WO-OPC-002", ProductCode="OP02", ProductName="OPC工单B", PlannedQty=60, Status="Pending" },
            };
        }

        /// <summary>上报生产结果（模拟写节点）。</summary>
        public async Task<bool> ReportResultAsync(MesReportResult result)
        {
            await Task.Delay(100);
            MessageReceived?.Invoke(this, $"[OPC-UA] WriteNode MES.ReportResult qty={result.Qty}");
            return true;
        }

        /// <summary>注册节点数据回调。</summary>
        public void Subscribe(Action<string, object> onDataReceived)
        {
            _dataHandler = onDataReceived;
        }

        /// <summary>节点变化模拟循环。</summary>
        private async Task SimulateNodeChangeAsync(CancellationToken ct)
        {
            var keys = _nodeValues.Keys.ToList();
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(1500).ContinueWith(_ => { });
                if (ct.IsCancellationRequested) break;

                var nodeId = keys[_rnd.Next(keys.Count)];
                var drift = (_rnd.NextDouble() - 0.5) * 2;
                _nodeValues[nodeId] = Math.Round(_nodeValues[nodeId] + drift, 2);
                _dataHandler?.Invoke(nodeId, _nodeValues[nodeId]);
                MessageReceived?.Invoke(this, $"[OPC-UA] DataChange {nodeId} = {_nodeValues[nodeId]}");
            }
        }
    }
}
