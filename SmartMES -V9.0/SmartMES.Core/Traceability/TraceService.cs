namespace SmartMES.Core.Traceability
{
    /// <summary>宸ュ簭璁板綍</summary>
    public class ProcessRecord
    {
        public Guid     Id         { get; set; } = Guid.NewGuid();
        public string   ProductSN  { get; set; } = string.Empty;
        public string   ProcessName{ get; set; } = string.Empty;
        public string   StationId  { get; set; } = string.Empty;
        public string   DeviceId   { get; set; } = string.Empty;
        public string   Operator   { get; set; } = string.Empty;
        public DateTime StartTime  { get; set; } = DateTime.Now;
        public DateTime? EndTime   { get; set; }
        public bool     IsPass     { get; set; } = true;
        public string   Result     { get; set; } = "OK";
        public Dictionary<string,string> Parameters { get; set; } = new();
        public string   ErrorMsg   { get; set; } = string.Empty;
        public TimeSpan Duration   => EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero;
    }

    /// <summary>浜у搧杩芥函璁板綍锛堣仛鍚堟墍鏈夊伐搴忥級</summary>
    public class ProductTrace
    {
        public string   SN          { get; set; } = string.Empty;
        public string   LotNo       { get; set; } = string.Empty;
        public string   ProductCode { get; set; } = string.Empty;
        public DateTime CreateTime  { get; set; } = DateTime.Now;
        public List<ProcessRecord> Processes { get; set; } = new();
        public bool IsPass => Processes.All(p => p.IsPass);
        public string FinalResult => IsPass ? "OK" : "NG";
    }

    /// <summary>杩芥函鏈嶅姟鎺ュ彛</summary>
    public interface ITraceService
    {
        ProductTrace StartTrace(string sn, string lotNo, string productCode);
        ProcessRecord StartProcess(string sn, string processName, string stationId, string deviceId);
        void EndProcess(Guid processId, bool isPass, string result = "OK", string errorMsg = "");
        void AddParameter(Guid processId, string key, string value);
        ProductTrace? GetTrace(string sn);
        IReadOnlyList<ProductTrace> GetAll();
        IReadOnlyList<ProductTrace> Query(DateTime from, DateTime to, bool? isPass = null);
    }

    /// <summary>
    /// 鐢熶骇杩芥函鏈嶅姟
    /// 璁板綍瀹屾暣鐢熶骇杩囩▼锛氫骇鍝丼N -> 宸ュ簭閾?-> 鍙傛暟/缁撴灉
    /// 瀹炵幇鍏ㄧ▼鍙拷婧紝鏀寔璐ㄩ噺鍒嗘瀽
    /// </summary>
    public class TraceService : ITraceService
    {
        private readonly Dictionary<string, ProductTrace> _traces = new();
        private readonly Dictionary<Guid, ProcessRecord> _processes = new();
        private readonly object _lock = new();

        /// <summary>
        /// 自动补齐：StartTrace 方法说明。
        /// </summary>
        public ProductTrace StartTrace(string sn, string lotNo, string productCode)
        {
            var trace = new ProductTrace
                { SN=sn, LotNo=lotNo, ProductCode=productCode };
            lock (_lock) _traces[sn] = trace;
            return trace;
        }

        /// <summary>
        /// 自动补齐：StartProcess 方法说明。
        /// </summary>
        public ProcessRecord StartProcess(
            string sn, string processName, string stationId, string deviceId)
        {
            var rec = new ProcessRecord
            {
                ProductSN=sn, ProcessName=processName,
                StationId=stationId, DeviceId=deviceId
            };
            lock (_lock)
            {
                _processes[rec.Id] = rec;
                if (_traces.TryGetValue(sn, out var trace))
                    trace.Processes.Add(rec);
            }
            return rec;
        }

        /// <summary>
        /// 自动补齐：EndProcess 方法说明。
        /// </summary>
        public void EndProcess(Guid processId, bool isPass,
            string result = "OK", string errorMsg = "")
        {
            lock (_lock)
            {
                if (!_processes.TryGetValue(processId, out var rec)) return;
                rec.EndTime = DateTime.Now;
                rec.IsPass  = isPass;
                rec.Result  = result;
                rec.ErrorMsg= errorMsg;
            }
        }

        /// <summary>
        /// 自动补齐：AddParameter 方法说明。
        /// </summary>
        public void AddParameter(Guid processId, string key, string value)
        {
            lock (_lock)
            {
                if (_processes.TryGetValue(processId, out var rec))
                    rec.Parameters[key] = value;
            }
        }

        /// <summary>
        /// 自动补齐：GetTrace 方法说明。
        /// </summary>
        public ProductTrace? GetTrace(string sn)
        { lock (_lock) return _traces.TryGetValue(sn, out var t) ? t : null; }

        /// <summary>
        /// 自动补齐：GetAll 方法说明。
        /// </summary>
        public IReadOnlyList<ProductTrace> GetAll()
        { lock (_lock) return _traces.Values.OrderByDescending(t => t.CreateTime).ToList(); }

        /// <summary>
        /// 自动补齐：Query 方法说明。
        /// </summary>
        public IReadOnlyList<ProductTrace> Query(DateTime from, DateTime to, bool? isPass = null)
        {
            lock (_lock)
                return _traces.Values
                    .Where(t => t.CreateTime >= from && t.CreateTime <= to
                        && (isPass == null || t.IsPass == isPass))
                    .OrderByDescending(t => t.CreateTime)
                    .ToList();
        }
    }
}
