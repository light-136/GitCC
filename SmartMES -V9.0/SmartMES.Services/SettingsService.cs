using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;
using System.Text.Json;

namespace SmartMES.Services
{
    /// <summary>
    /// 配置服务实现。
    /// 使用 JSON 将系统配置持久化到本地文件。
    /// </summary>
    public class SettingsService : ISettingsService
    {
        /// <summary>配置文件路径。</summary>
        private readonly string _filePath;

        /// <summary>当前配置对象。</summary>
        public SystemSettings Settings { get; private set; } = new SystemSettings();

        /// <summary>配置变更事件。</summary>
        public event EventHandler? SettingsChanged;

        /// <summary>JSON 序列化选项。</summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>构造配置服务并指定配置文件路径。</summary>
        public SettingsService(string filePath = "settings.json")
        {
            _filePath = filePath;
        }

        /// <summary>保存当前配置到本地 JSON 文件。</summary>
        public async Task SaveAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(Settings, _jsonOptions);
                await File.WriteAllTextAsync(_filePath, json, System.Text.Encoding.UTF8);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[配置保存失败] {ex.Message}");
                throw;
            }
        }

        /// <summary>从本地 JSON 文件加载配置。</summary>
        public async Task LoadAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    Settings = new SystemSettings();
                    await SaveAsync();
                    return;
                }

                var json = await File.ReadAllTextAsync(_filePath, System.Text.Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<SystemSettings>(json, _jsonOptions);
                if (loaded != null)
                    Settings = loaded;

                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[配置加载失败] {ex.Message}，使用默认配置");
                Settings = new SystemSettings();
            }
        }
    }
}
