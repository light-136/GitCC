using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface ISettingsService
{
    SystemSettings Current { get; }
    void Save();
    void Reload();
    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
}
