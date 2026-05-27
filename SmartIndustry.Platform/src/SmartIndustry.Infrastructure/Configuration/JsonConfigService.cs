// ============================================================
// 文件：JsonConfigService.cs
// 层次：基础设施层 (Infrastructure Layer) — JSON 配置服务
// 职责：
//   提供 JSON 配置文件的读取、热重载和分节访问能力：
//   - 从 appsettings.json 读取配置（System.Text.Json）
//   - FileSystemWatcher 监听文件变更，自动热重载（无需重启）
//   - GetSection<T>：将配置的某个节（Section）反序列化为强类型对象
//   - ConfigChanged 事件：配置变更时通知订阅者（UI/Application 层响应更新）
// 设计思路：
//   简单的本地配置需求（不需要 Microsoft.Extensions.Configuration 的全部功能）
//   用此轻量实现替代，避免 Desktop 应用引入过多 ASP.NET Core 依赖。
//   热重载使用防抖（Debounce）处理：FileSystemWatcher 可能在文件保存时
//   快速触发多次事件（如 JetBrains Rider 先清空再写入），防抖确保只处理最终状态。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartIndustry.Infrastructure.Configuration
{
    /// <summary>
    /// JSON 配置服务。
    /// 提供强类型配置读取、热重载和变更通知功能。
    /// </summary>
    public class JsonConfigService : IDisposable
    {
        // ----------------------------------------------------------------
        // 字段
        // ----------------------------------------------------------------

        /// <summary>配置文件完整路径</summary>
        private readonly string _configFilePath;

        /// <summary>
        /// 文件监视器（监听配置文件变更，触发热重载）
        /// </summary>
        private FileSystemWatcher? _watcher;

        /// <summary>当前配置的 JSON 根节点（线程安全读取，通过 _lock 保护写入）</summary>
        private JsonNode? _configRoot;

        /// <summary>读写锁：保护 _configRoot 的并发访问（多读单写，性能优于 lock）</summary>
        private readonly ReaderWriterLockSlim _lock = new();

        /// <summary>热重载防抖计时器（避免 FileSystemWatcher 短时多次触发）</summary>
        private Timer? _debounceTimer;

        /// <summary>防抖延迟（毫秒，等待文件写入完成后再重载）</summary>
        private const int DebounceMs = 300;

        // ----------------------------------------------------------------
        // JSON 选项（全局复用，避免重复构造）
        // ----------------------------------------------------------------

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            AllowTrailingCommas = true,          // 允许 JSON 末尾逗号（方便手动编辑）
            ReadCommentHandling = JsonCommentHandling.Skip  // 允许 JSON5 风格注释
        };

        // ----------------------------------------------------------------
        // 外部事件
        // ----------------------------------------------------------------

        /// <summary>
        /// 配置文件变更通知事件。
        /// 热重载完成后触发，参数为变更的节名称（null=整个文件重载）。
        /// UI 和 Application 层订阅此事件以刷新配置相关功能。
        /// </summary>
        public event Action<string?>? ConfigChanged;

        /// <summary>
        /// 构造函数：加载配置文件并启动文件监视
        /// </summary>
        /// <param name="configFilePath">配置文件路径（绝对或相对路径，默认 appsettings.json）</param>
        public JsonConfigService(string? configFilePath = null)
        {
            _configFilePath = configFilePath != null
                ? Path.GetFullPath(configFilePath)
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

            // 初始加载配置（如果文件不存在则创建空配置）
            LoadConfig();

            // 启动文件变更监视
            SetupFileWatcher();
        }

        // ================================================================
        // 公共 API
        // ================================================================

        /// <summary>
        /// 获取整个配置的指定节，反序列化为强类型 T 对象。
        /// 节路径使用冒号分隔（如 "Database:ConnectionString"）。
        /// </summary>
        /// <typeparam name="T">配置节的目标类型</typeparam>
        /// <param name="sectionPath">节路径（冒号分隔，如 "Mqtt:BrokerHost"）</param>
        /// <returns>反序列化的配置对象，或 T 的默认值（节不存在时）</returns>
        public T? GetSection<T>(string sectionPath)
        {
            if (string.IsNullOrWhiteSpace(sectionPath))
                throw new ArgumentException("配置节路径不能为空", nameof(sectionPath));

            _lock.EnterReadLock();
            try
            {
                if (_configRoot == null) return default;

                // 按冒号分割路径，逐层导航 JSON 树
                var parts = sectionPath.Split(':');
                JsonNode? current = _configRoot;

                foreach (var part in parts)
                {
                    current = current?[part];
                    if (current == null) return default;
                }

                // 将找到的节反序列化为 T
                return current.Deserialize<T>(JsonOptions);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// 获取指定键的字符串值（适合读取简单的字符串配置）
        /// </summary>
        /// <param name="keyPath">键路径（冒号分隔，如 "Database:Provider"）</param>
        /// <param name="defaultValue">键不存在时的默认值</param>
        public string? GetValue(string keyPath, string? defaultValue = null)
        {
            return GetSection<string>(keyPath) ?? defaultValue;
        }

        /// <summary>
        /// 获取指定键的整型值
        /// </summary>
        public int GetInt(string keyPath, int defaultValue = 0)
        {
            return GetSection<int?>(keyPath) ?? defaultValue;
        }

        /// <summary>
        /// 获取指定键的布尔值
        /// </summary>
        public bool GetBool(string keyPath, bool defaultValue = false)
        {
            return GetSection<bool?>(keyPath) ?? defaultValue;
        }

        /// <summary>
        /// 手动触发配置重载（如在测试中替换了文件内容后手动调用）
        /// </summary>
        public void Reload()
        {
            LoadConfig();
            ConfigChanged?.Invoke(null);
        }

        // ================================================================
        // 私有实现
        // ================================================================

        /// <summary>
        /// 从磁盘加载配置文件（线程安全）。
        /// 文件不存在时创建一个包含空 JSON 对象的配置文件。
        /// 文件解析失败时保留旧配置（避免热重载时无效 JSON 导致配置丢失）。
        /// </summary>
        private void LoadConfig()
        {
            string json;

            if (!File.Exists(_configFilePath))
            {
                // 配置文件不存在：创建默认空配置文件
                json = "{}";
                var dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_configFilePath, json);
            }
            else
            {
                try
                {
                    json = File.ReadAllText(_configFilePath);
                }
                catch (IOException)
                {
                    // 文件正在被写入（如 IDE 保存时），稍后重试（防抖会处理）
                    return;
                }
            }

            try
            {
                var newRoot = JsonNode.Parse(json, nodeOptions: null,
                    documentOptions: new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    });

                // 写锁保护替换操作
                _lock.EnterWriteLock();
                try
                {
                    _configRoot = newRoot;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            catch (JsonException)
            {
                // JSON 解析失败：保留旧配置，记录错误（此处没有 Logger 依赖，写 Debug 输出）
                System.Diagnostics.Debug.WriteLine($"[JsonConfigService] 配置文件 JSON 解析失败：{_configFilePath}");
            }
        }

        /// <summary>
        /// 启动 FileSystemWatcher 监听配置文件变更。
        /// 使用防抖定时器（300ms），防止编辑器保存时连续多次触发重载。
        /// </summary>
        private void SetupFileWatcher()
        {
            var directory = Path.GetDirectoryName(_configFilePath);
            var fileName = Path.GetFileName(_configFilePath);

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

            _watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            // 文件变更事件（使用 Changed，Created 事件在某些编辑器保存时触发）
            _watcher.Changed += OnConfigFileChanged;
            _watcher.Created += OnConfigFileChanged;
        }

        /// <summary>
        /// 配置文件变更事件处理器（FileSystemWatcher 回调，在后台线程执行）。
        /// 重置防抖计时器，300ms 后执行实际重载。
        /// </summary>
        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            // 取消之前的防抖定时器，重新计时（处理快速连续变更）
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                LoadConfig();
                // 在线程池线程触发事件（调用方负责线程安全处理）
                ConfigChanged?.Invoke(null);
            }, null, DebounceMs, Timeout.Infinite);
        }

        // ================================================================
        // IDisposable
        // ================================================================

        public void Dispose()
        {
            _watcher?.Dispose();
            _debounceTimer?.Dispose();
            _lock.Dispose();
        }
    }
}
