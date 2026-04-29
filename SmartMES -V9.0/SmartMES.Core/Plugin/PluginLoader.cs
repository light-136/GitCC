using System.Reflection;

namespace SmartMES.Core.Plugin
{
    /// <summary>鎻掍欢鎺ュ彛锛氬畾涔夋彃浠剁敓鍛藉懆鏈熶笌鍏冩暟鎹绾?/summary>
    public interface IPlugin
    {
        /// <summary>鎻掍欢鍚嶇О锛堝敮涓€鏍囪瘑锛?/summary>
        string Name        { get; }
        /// <summary>鎻掍欢鐗堟湰鍙?/summary>
        string Version     { get; }
        /// <summary>鎻掍欢鍔熻兘鎻忚堪</summary>
        string Description { get; }
        /// <summary>鍒濆鍖栨彃浠跺苟娉ㄥ叆涓婁笅鏂囨湇鍔?/summary>
        void Initialize(IPluginContext context);
        /// <summary>鎻掍欢鍗歌浇鍓嶇殑璧勬簮閲婃斁鍏ュ彛</summary>
        void Shutdown();
    }

    /// <summary>鎻掍欢涓婁笅鏂囷細鍚戞彃浠舵彁渚涙棩蹇椼€佷簨浠朵笌鏈嶅姟璁块棶鑳藉姏</summary>
    public interface IPluginContext
    {
        /// <summary>鍐欏叆鎻掍欢杩愯鏃ュ織</summary>
        void Log(string message);
        /// <summary>鍙戝竷鎻掍欢浜嬩欢鍒板涓荤郴缁?/summary>
        void PublishEvent<T>(T @event);
        /// <summary>鎸夌被鍨嬭幏鍙栧涓绘湇鍔″疄渚?/summary>
        T? GetService<T>() where T : class;
    }

    /// <summary>鎻掍欢淇℃伅</summary>
    public class PluginInfo
    {
        public string Name        { get; set; } = string.Empty;
        public string Version     { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FilePath    { get; set; } = string.Empty;
        public bool   IsLoaded    { get; set; }
        public bool   IsActive    { get; set; }
        public string LoadError   { get; set; } = string.Empty;
    }

    /// <summary>
    /// 鎻掍欢鍔犺浇鍣?鈥?鍔ㄦ€佸姞杞紻LL锛屾敮鎸佺儹鎻掓嫈
    /// 鎵弿plugins鐩綍涓嬫墍鏈夊疄鐜癐Plugin鎺ュ彛鐨勭▼搴忛泦
    /// </summary>
    public class PluginLoader
    {
        private readonly Dictionary<string, IPlugin>    _plugins = new();
        private readonly Dictionary<string, PluginInfo> _infos   = new();
        private readonly IPluginContext _context;
        private readonly object _lock = new();

        public event EventHandler<PluginInfo>? PluginLoaded;
        public event EventHandler<PluginInfo>? PluginUnloaded;

        /// <summary>
        /// 自动补齐：PluginLoader 方法说明。
        /// </summary>
        public PluginLoader(IPluginContext context) { _context = context; }

        /// <summary>
        /// 自动补齐：GetAll 方法说明。
        /// </summary>
        public IReadOnlyList<PluginInfo> GetAll()
        { lock (_lock) return _infos.Values.ToList(); }

        /// <summary>鎵弿鐩綍鍔犺浇鎵€鏈夋彃浠禗LL</summary>
        public List<PluginInfo> ScanDirectory(string directory)
        {
            var results = new List<PluginInfo>();
            if (!Directory.Exists(directory)) return results;
            foreach (var dll in Directory.GetFiles(directory, "*.dll"))
                results.Add(LoadPlugin(dll));
            return results;
        }

        /// <summary>鍔犺浇鍗曚釜鎻掍欢DLL</summary>
        public PluginInfo LoadPlugin(string dllPath)
        {
            var info = new PluginInfo { FilePath = dllPath };
            try
            {
                var asm = Assembly.LoadFrom(dllPath);
                var types = asm.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t)
                        && !t.IsInterface && !t.IsAbstract);

                foreach (var type in types)
                {
                    if (Activator.CreateInstance(type) is not IPlugin plugin) continue;
                    plugin.Initialize(_context);
                    info.Name = plugin.Name;
                    info.Version = plugin.Version;
                    info.Description = plugin.Description;
                    info.IsLoaded = true;
                    info.IsActive = true;
                    lock (_lock) { _plugins[plugin.Name] = plugin; _infos[plugin.Name] = info; }
                    PluginLoaded?.Invoke(this, info);
                }
            }
            catch (Exception ex)
            {
                info.LoadError = ex.Message;
                info.IsLoaded  = false;
            }
            return info;
        }

        /// <summary>鍗歌浇鎻掍欢</summary>
        public bool UnloadPlugin(string name)
        {
            lock (_lock)
            {
                if (!_plugins.TryGetValue(name, out var plugin)) return false;
                try { plugin.Shutdown(); } catch { /* 鍗歌浇澶辫触涓嶅穿婧?*/ }
                _plugins.Remove(name);
                if (_infos.TryGetValue(name, out var info))
                {
                    info.IsActive = false;
                    PluginUnloaded?.Invoke(this, info);
                }
                return true;
            }
        }

        /// <summary>璋冪敤鎵€鏈夋彃浠剁殑鎸囧畾鏂规硶锛堝箍鎾ā寮忥級</summary>
        public void BroadcastToAll(Action<IPlugin> action)
        {
            List<IPlugin> copy;
            lock (_lock) copy = _plugins.Values.ToList();
            foreach (var p in copy)
            {
                try { action(p); }
                catch { /* 鍗曚釜鎻掍欢鏁呴殰涓嶅奖鍝嶅叾浠栨彃浠?*/ }
            }
        }
    }

    /// <summary>鍐呯疆涓婁笅鏂囧疄鐜帮紙鐢ㄤ簬婕旂ず锛?/summary>
    public class DefaultPluginContext : IPluginContext
    {
        private readonly Action<string> _logAction;
        /// <summary>
        /// 自动补齐：DefaultPluginContext 方法说明。
        /// </summary>
        public DefaultPluginContext(Action<string> logAction) { _logAction = logAction; }
        /// <summary>
        /// 自动补齐：Log 方法说明。
        /// </summary>
        public void Log(string message) => _logAction(message);
        public void PublishEvent<T>(T @event) { }
        public T? GetService<T>() where T : class => null;
    }
}
