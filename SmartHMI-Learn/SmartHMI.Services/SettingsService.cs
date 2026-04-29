using System.Text.Json;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Services;

public class SettingsService : ISettingsService
{
    private SystemSettings _settings = new();
    private readonly string _filePath;

    public SystemSettings Current => _settings;

    public SettingsService(string filePath = "appsettings.json")
    {
        _filePath = filePath;
        Reload();
    }

    public void Reload()
    {
        if (!File.Exists(_filePath)) { Save(); return; }
        try
        {
            var json = File.ReadAllText(_filePath);
            _settings = JsonSerializer.Deserialize<SystemSettings>(json) ?? new SystemSettings();
        }
        catch { _settings = new SystemSettings(); }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    public T Get<T>(string key, T defaultValue)
    {
        var prop = typeof(SystemSettings).GetProperty(key);
        if (prop == null) return defaultValue;
        var val = prop.GetValue(_settings);
        return val is T t ? t : defaultValue;
    }

    public void Set<T>(string key, T value)
    {
        var prop = typeof(SystemSettings).GetProperty(key);
        prop?.SetValue(_settings, value);
    }
}
