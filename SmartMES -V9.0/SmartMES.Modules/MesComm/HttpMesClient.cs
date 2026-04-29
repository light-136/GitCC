using System.Net.Http;
using System.Text.Json;

namespace SmartMES.Modules.MesComm
{
    /// <summary>
    /// HTTP REST MES客户端实现。
    /// </summary>
    public class HttpMesClient : IMesClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private bool _isConnected;
        private Action<string, object>? _dataHandler;

        public string ProtocolName => "HTTP REST";
        public bool IsConnected => _isConnected;
        public event EventHandler<string>? MessageReceived;

        private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

        /// <summary>构造 HTTP MES 客户端。</summary>
        public HttpMesClient(string baseUrl = "http://localhost:8080/api")
        {
            _baseUrl = baseUrl;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        /// <summary>建立 HTTP 连接（模拟）。</summary>
        public async Task ConnectAsync()
        {
            await Task.Delay(300);
            _isConnected = true;
            MessageReceived?.Invoke(this, $"[HTTP] 已连接到MES: {_baseUrl}");
        }

        /// <summary>断开 HTTP 连接（模拟）。</summary>
        public async Task DisconnectAsync()
        {
            await Task.Delay(100);
            _isConnected = false;
            MessageReceived?.Invoke(this, "[HTTP] 已断开MES连接");
        }

        /// <summary>获取工单列表（模拟）。</summary>
        public async Task<List<MesWorkOrder>> GetWorkOrdersAsync()
        {
            await Task.Delay(200);
            var rnd = new Random();
            var orders = new List<MesWorkOrder>
            {
                new() { OrderId=$"WO-{rnd.Next(1000,9999)}", ProductCode="P001", ProductName="精密齿轮", PlannedQty=100, ActualQty=rnd.Next(0,100), Status="Running" },
                new() { OrderId=$"WO-{rnd.Next(1000,9999)}", ProductCode="P002", ProductName="轴承座", PlannedQty=50, ActualQty=50, Status="Done" },
                new() { OrderId=$"WO-{rnd.Next(1000,9999)}", ProductCode="P003", ProductName="连接法兰", PlannedQty=200, ActualQty=0, Status="Pending" },
            };
            MessageReceived?.Invoke(this, $"[HTTP] GET /workorders -> {orders.Count}条");
            return orders;
        }

        /// <summary>上报生产结果（模拟）。</summary>
        public async Task<bool> ReportResultAsync(MesReportResult result)
        {
            await Task.Delay(150);
            var json = JsonSerializer.Serialize(result);
            MessageReceived?.Invoke(this, $"[HTTP] POST /report -> {json}");
            return true;
        }

        /// <summary>注册订阅回调并启动轮询模拟。</summary>
        public void Subscribe(Action<string, object> onDataReceived)
        {
            _dataHandler = onDataReceived;
            _ = PollAsync();
        }

        /// <summary>后台轮询模拟推送。</summary>
        private async Task PollAsync()
        {
            while (_isConnected)
            {
                await Task.Delay(5000);
                if (!_isConnected) break;
                var rnd = new Random();
                _dataHandler?.Invoke("MES.ProductionCount", rnd.Next(100, 999));
            }
        }
    }
}
