using System.Globalization;

namespace IndustrialVoiceRecorder.Shared;

/// <summary>
/// 应用程序配置（从config.txt读取）
/// 所有配置项都有默认值，config.txt不存在也不崩溃
/// </summary>
public static class AppConfig
{
    private static readonly Dictionary<string, string> _config = new(StringComparer.OrdinalIgnoreCase);

    // ==================== FunASR ====================
    public static string FunAsrHost => Get("FunASR.Host", "127.0.0.1");
    public static int FunAsrPort => GetInt("FunASR.Port", 10095);
    public static string FunAsrEndpoint => Get("FunASR.Endpoint", "/asr");
    public static int FunAsrTimeoutSeconds => GetInt("FunASR.TimeoutSeconds", 30);
    public static string FunAsrUrl => $"http://{FunAsrHost}:{FunAsrPort}";

    // ==================== 数据库 ====================
    public static string DatabaseFileName => Get("Database.FileName", "voice_records.db");
    public static string DatabaseDirectory => Get("Database.Directory", "Data");

    // ==================== 目录 ====================
    public static string ExportDirectory => Get("Export.Directory", "Export");
    public static string LogDirectory => Get("Log.Directory", "Logs");
    public static string ModelDirectory => Get("Model.Directory", "Models");
    public static string AudioSaveDirectory => Get("Audio.SaveDirectory", "AudioFiles");

    // ==================== 音频 ====================
    public static int AudioSampleRate => GetInt("Audio.SampleRate", 16000);
    public static int AudioChannels => GetInt("Audio.Channels", 1);
    public static int AudioBitsPerSample => GetInt("Audio.BitsPerSample", 16);

    // ==================== VAD ====================
    public static int VadSilenceTimeoutMs => GetInt("VAD.SilenceTimeoutMs", 2000);
    public static int VadMinAudioBytes => GetInt("VAD.MinAudioBytes", 48000);
    public static int VadMaxBufferBytes => GetInt("VAD.MaxBufferBytes", 480000);
    public static int VadPreBufferBytes => GetInt("VAD.PreBufferBytes", 16000);
    public static float VadEnergyMultiplier => GetFloat("VAD.EnergyMultiplier", 2.5f);
    public static float VadMinThreshold => GetFloat("VAD.MinThreshold", 0.0003f);

    // ==================== 应用 ====================
    public static string AppName => Get("App.Name", "工业语音记录系统");
    public static string AppVersion => Get("App.Version", "3.0.0");

    /// <summary>
    /// 从config.txt加载配置
    /// </summary>
    public static void Load(string configPath)
    {
        _config.Clear();
        if (!File.Exists(configPath)) return;

        try
        {
            foreach (var line in File.ReadAllLines(configPath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;

                var eqIndex = trimmed.IndexOf('=');
                if (eqIndex > 0)
                {
                    var key = trimmed[..eqIndex].Trim();
                    var value = trimmed[(eqIndex + 1)..].Trim();
                    _config[key] = value;
                }
            }
        }
        catch { /* 读取失败使用默认值 */ }
    }

    public static string Get(string key, string defaultValue)
        => _config.TryGetValue(key, out var v) ? v : defaultValue;

    public static int GetInt(string key, int defaultValue)
        => _config.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : defaultValue;

    public static float GetFloat(string key, float defaultValue)
        => _config.TryGetValue(key, out var v) && float.TryParse(v, CultureInfo.InvariantCulture, out var f) ? f : defaultValue;
}
