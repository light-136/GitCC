using SmartMES.Core.Models;
using System.Text.Json;

namespace SmartMES.Services
{
    /// <summary>
    /// 扩展配置中心服务（ConfigurationCenter）。
    /// 在原有 SettingsService 基础上，增加以下能力：
    ///   1. 分层配置（AppConfiguration 按功能域分组）
    ///   2. 配置验证（值范围检查）
    ///   3. 配置导入导出（JSON 文件）
    ///   4. 配置变更日志
    ///   5. 环境配置切换（dev / prod / test）
    /// 与现有 SettingsService 并存，不破坏已有代码。
    /// </summary>
    public class ConfigurationCenter
    {
        // ──────── 私有字段 ────────
        private readonly string _configDir;             // 配置文件目录
        private readonly string _configFile;            // 主配置文件路径
        private readonly List<string> _changeLog = new(); // 配置变更历史记录

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // ──────── 公开属性 ────────
        /// <summary>当前生效的分层配置对象</summary>
        public AppConfiguration Config { get; private set; } = new AppConfiguration();

        /// <summary>当前环境名称（dev / prod / test）</summary>
        public string Environment { get; private set; } = "prod";

        /// <summary>配置变更历史（最近 100 条）</summary>
        public IReadOnlyList<string> ChangeLog => _changeLog.AsReadOnly();

        // ──────── 事件 ────────
        /// <summary>配置重载完成时触发</summary>
        public event EventHandler<AppConfiguration>? ConfigurationLoaded;

        /// <summary>配置保存完成时触发</summary>
        public event EventHandler? ConfigurationSaved;

        /// <summary>
        /// 创建配置中心，指定配置目录和环境名。
        /// </summary>
        /// <param name="configDir">配置文件存放目录（默认 Config）</param>
        /// <param name="environment">环境名：dev / prod / test（默认 prod）</param>
        public ConfigurationCenter(string configDir = "Config", string environment = "prod")
        {
            _configDir  = configDir;
            Environment = environment;
            _configFile = Path.Combine(_configDir, $"appsettings.{environment}.json");
        }

        /// <summary>
        /// 从文件加载配置。若文件不存在则创建默认配置文件。
        /// </summary>
        public async Task LoadAsync()
        {
            Directory.CreateDirectory(_configDir);

            if (!File.Exists(_configFile))
            {
                // 首次运行：写入默认配置
                Config = new AppConfiguration();
                await SaveAsync();
                AddChangeLog("初始化默认配置文件");
                return;
            }

            var json = await File.ReadAllTextAsync(_configFile, System.Text.Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<AppConfiguration>(json, _jsonOptions);
            if (loaded != null)
            {
                Config = loaded;
                AddChangeLog($"配置加载成功（环境：{Environment}）");
            }

            ConfigurationLoaded?.Invoke(this, Config);
        }

        /// <summary>
        /// 保存当前配置到文件。
        /// </summary>
        public async Task SaveAsync()
        {
            Directory.CreateDirectory(_configDir);
            var json = JsonSerializer.Serialize(Config, _jsonOptions);
            await File.WriteAllTextAsync(_configFile, json, System.Text.Encoding.UTF8);
            AddChangeLog($"配置保存成功 -> {_configFile}");
            ConfigurationSaved?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 将配置导出到指定文件路径（用于备份或移机）。
        /// </summary>
        /// <param name="exportPath">导出文件路径</param>
        public async Task ExportAsync(string exportPath)
        {
            var json = JsonSerializer.Serialize(Config, _jsonOptions);
            await File.WriteAllTextAsync(exportPath, json, System.Text.Encoding.UTF8);
            AddChangeLog($"配置导出 -> {exportPath}");
        }

        /// <summary>
        /// 从指定文件导入配置并立即生效。
        /// </summary>
        /// <param name="importPath">导入文件路径</param>
        public async Task ImportAsync(string importPath)
        {
            var json = await File.ReadAllTextAsync(importPath, System.Text.Encoding.UTF8);
            var imported = JsonSerializer.Deserialize<AppConfiguration>(json, _jsonOptions);
            if (imported == null)
                throw new InvalidDataException("导入的配置文件格式无效");

            Validate(imported);  // 先验证，验证通过再替换
            Config = imported;
            await SaveAsync();
            AddChangeLog($"配置从 {importPath} 导入成功");
            ConfigurationLoaded?.Invoke(this, Config);
        }

        /// <summary>
        /// 配置验证：检查关键参数的值范围，违规时抛出异常。
        /// </summary>
        /// <param name="cfg">待验证的配置对象（默认验证当前 Config）</param>
        public void Validate(AppConfiguration? cfg = null)
        {
            cfg ??= Config;
            var errors = new List<string>();

            if (cfg.TcpServerPort < 1 || cfg.TcpServerPort > 65535)
                errors.Add($"TCP端口超出范围：{cfg.TcpServerPort}（应 1-65535）");

            if (cfg.ModbusPort < 1 || cfg.ModbusPort > 65535)
                errors.Add($"Modbus端口超出范围：{cfg.ModbusPort}（应 1-65535）");

            if (cfg.DataSamplingIntervalMs < 10 || cfg.DataSamplingIntervalMs > 60000)
                errors.Add($"采样间隔超出范围：{cfg.DataSamplingIntervalMs}ms（应 10-60000）");

            if (cfg.LogRetentionDays < 1 || cfg.LogRetentionDays > 365)
                errors.Add($"日志保留天数超出范围：{cfg.LogRetentionDays}（应 1-365）");

            if (cfg.MaxLogEntries < 100 || cfg.MaxLogEntries > 100000)
                errors.Add($"最大日志条数超出范围：{cfg.MaxLogEntries}（应 100-100000）");

            if (cfg.TemperatureAlarmThreshold < 0 || cfg.TemperatureAlarmThreshold > 500)
                errors.Add($"温度阈值超出范围：{cfg.TemperatureAlarmThreshold}（应 0-500）");

            if (errors.Count > 0)
                throw new ArgumentException("配置验证失败：\n" + string.Join("\n", errors));
        }

        /// <summary>
        /// 更新单个配置项并记录变更日志（支持链式调用）。
        /// </summary>
        /// <typeparam name="T">属性类型</typeparam>
        /// <param name="setter">配置项修改委托（例如 cfg => cfg.TcpServerIp = "192.168.1.1"）</param>
        /// <param name="description">变更说明（用于日志）</param>
        public ConfigurationCenter Set<T>(Action<AppConfiguration> setter, string description = "")
        {
            setter(Config);
            AddChangeLog(string.IsNullOrEmpty(description) ? "配置项更新" : description);
            return this;  // 支持链式调用
        }

        /// <summary>
        /// 获取 Modbus TCP 连接地址字符串（host:port）。
        /// </summary>
        public string GetModbusEndpoint() => $"{Config.ModbusHostIp}:{Config.ModbusPort}";

        /// <summary>
        /// 判断当前是否为仿真运行模式。
        /// </summary>
        public bool IsSimulationMode => Config.RunMode.Equals("Simulation", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 列出所有可用的配置文件（用于多环境切换展示）。
        /// </summary>
        public IEnumerable<string> GetAvailableEnvironments()
        {
            if (!Directory.Exists(_configDir)) return Enumerable.Empty<string>();

            return Directory.GetFiles(_configDir, "appsettings.*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f)
                    .Replace("appsettings.", "", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>添加变更日志记录（保留最近 100 条）</summary>
        private void AddChangeLog(string message)
        {
            _changeLog.Insert(0, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            if (_changeLog.Count > 100)
                _changeLog.RemoveAt(_changeLog.Count - 1);
        }
    }
}
