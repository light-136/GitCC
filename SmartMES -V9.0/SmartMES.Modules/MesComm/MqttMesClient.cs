namespace SmartMES.Modules.MesComm
{
    /// <summary>
    /// MQTT MES瀹㈡埛绔疄鐜帮紙妯℃嫙锛?
    /// 鐢熶骇鐜鏇挎崲涓?MQTTnet 搴撳疄鐜扮湡瀹濵QTT杩炴帴
    /// MQTT鏄伐涓欼oT鏈€甯哥敤鐨勮交閲忕骇鍙戝竷璁㈤槄鍗忚
    /// </summary>
    public class MqttMesClient : IMesClient
    {
        private bool _isConnected;
        private readonly string _broker;
        private readonly int _port;
        private Action<string, object>? _dataHandler;
        private CancellationTokenSource? _cts;
        private readonly Random _rnd = new();

        public string ProtocolName => "MQTT";
        public bool IsConnected => _isConnected;
        public event EventHandler<string>? MessageReceived;

        // 妯℃嫙璁㈤槄鐨凾opic鍒楄〃
        private readonly string[] _topics = {
            "factory/line1/temperature",
            "factory/line1/pressure",
            "factory/mes/workorder"
        };

        /// <summary>
        /// 自动补齐：MqttMesClient 方法说明。
        /// </summary>
        public MqttMesClient(string broker = "127.0.0.1", int port = 1883)
        {
            _broker = broker;
            _port = port;
        }

        /// <summary>寤虹珛MQTT杩炴帴骞跺紑鍚ā鎷熸帹閫?/summary>
        public async Task ConnectAsync()
        {
            await Task.Delay(400);
            _isConnected = true;
            _cts = new CancellationTokenSource();
            MessageReceived?.Invoke(this, $"[MQTT] 宸茶繛鎺ュ埌Broker: {_broker}:{_port}");
            // 鍚姩妯℃嫙娑堟伅鎺ㄩ€?
            _ = SimulatePushAsync(_cts.Token);
        }

        /// <summary>鏂紑MQTT杩炴帴骞跺仠姝㈡秷鎭ā鎷?/summary>
        public async Task DisconnectAsync()
        {
            _cts?.Cancel();
            await Task.Delay(100);
            _isConnected = false;
            MessageReceived?.Invoke(this, "[MQTT] 宸叉柇寮€Broker杩炴帴");
        }

        /// <summary>MQTT妯″紡涓嶆敮鎸佽姹?鍝嶅簲锛岃繑鍥炴ā鎷熸暟鎹?/summary>
        /// <summary>鑾峰彇宸ュ崟鍒楄〃锛圡QTT鍦烘櫙浣跨敤妯℃嫙缂撳瓨杩斿洖锛?/summary>
        public async Task<List<MesWorkOrder>> GetWorkOrdersAsync()
        {
            await Task.Delay(100);
            return new List<MesWorkOrder>
            {
                new() { OrderId="WO-MQTT-001", ProductCode="PM01", ProductName="MQTT宸ュ崟", PlannedQty=80, Status="Running" }
            };
        }

        /// <summary>鍙戝竷鐢熶骇缁撴灉鍒版寚瀹氫富棰?/summary>
        public async Task<bool> ReportResultAsync(MesReportResult result)
        {
            await Task.Delay(50);
            var topic = $"factory/mes/report/{result.OrderId}";
            MessageReceived?.Invoke(this, $"[MQTT] PUBLISH {topic} qty={result.Qty} pass={result.IsPass}");
            return true;
        }

        /// <summary>娉ㄥ唽MQTT娑堟伅鎺ユ敹鍥炶皟骞惰緭鍑鸿闃呯姸鎬?/summary>
        public void Subscribe(Action<string, object> onDataReceived)
        {
            _dataHandler = onDataReceived;
            MessageReceived?.Invoke(this, $"[MQTT] 宸茶闃?{_topics.Length} 涓猅opic");
        }

        /// <summary>妯℃嫙MQTT Broker鎸佺画鎺ㄩ€佹秷鎭?/summary>
        /// <summary>鍚庡彴寰幆锛氭ā鎷烞roker鎸塗opic鎺ㄩ€佹祴鐐瑰€?/summary>
        private async Task SimulatePushAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct).ContinueWith(_ => { });
                if (ct.IsCancellationRequested) break;

                var topic = _topics[_rnd.Next(_topics.Length)];
                var value = _rnd.NextDouble() * 100;
                _dataHandler?.Invoke(topic, Math.Round(value, 2));
                MessageReceived?.Invoke(this, $"[MQTT] 鈫?{topic}: {value:F2}");
            }
        }
    }
}
