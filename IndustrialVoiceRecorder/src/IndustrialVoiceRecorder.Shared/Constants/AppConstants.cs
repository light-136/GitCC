namespace IndustrialVoiceRecorder.Shared.Constants;

/// <summary>
/// 应用程序常量
/// 集中管理所有常量值
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// 应用名称
    /// </summary>
    public const string AppName = "工业语音记录系统";

    /// <summary>
    /// 版本号
    /// </summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// 数据库文件名
    /// </summary>
    public const string DatabaseFileName = "voice_records.db";

    /// <summary>
    /// 默认Whisper模型文件名
    /// </summary>
    public const string DefaultModelFileName = "ggml-base.bin";

    /// <summary>
    /// 音频采样率（Hz）
    /// </summary>
    public const int SampleRate = 16000;

    /// <summary>
    /// 音频声道数
    /// </summary>
    public const int Channels = 1;

    /// <summary>
    /// 音频位深度
    /// </summary>
    public const int BitsPerSample = 16;

    /// <summary>
    /// 导出目录名
    /// </summary>
    public const string ExportDirectoryName = "Export";

    /// <summary>
    /// 日志目录名
    /// </summary>
    public const string LogDirectoryName = "Logs";

    /// <summary>
    /// 模型目录名
    /// </summary>
    public const string ModelDirectoryName = "Models";
}
