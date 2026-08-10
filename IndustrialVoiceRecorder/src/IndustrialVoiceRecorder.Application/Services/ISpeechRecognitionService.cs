namespace IndustrialVoiceRecorder.Application.Services;

/// <summary>
/// 语音识别服务接口
/// 负责将音频数据转换为文本
/// </summary>
public interface ISpeechRecognitionService
{
    /// <summary>
    /// 初始化识别引擎
    /// </summary>
    /// <param name="modelPath">模型文件路径</param>
    Task InitializeAsync(string modelPath);

    /// <summary>
    /// 识别音频数据
    /// </summary>
    /// <param name="audioData">音频字节数据</param>
    /// <returns>识别结果</returns>
    Task<RecognitionResult> RecognizeAsync(byte[] audioData);

    /// <summary>
    /// 流式识别（实时）
    /// </summary>
    /// <param name="audioStream">音频流</param>
    /// <returns>识别文本</returns>
    Task<string> RecognizeStreamAsync(Stream audioStream);

    /// <summary>
    /// 是否可用（引擎已初始化）
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 识别完成事件
    /// </summary>
    event EventHandler<RecognitionCompletedEventArgs>? RecognitionCompleted;
}

/// <summary>
/// 识别结果
/// </summary>
public class RecognitionResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 识别的文本
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 置信度（0-1）
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 处理时长（毫秒）
    /// </summary>
    public long ProcessingTimeMs { get; set; }
}

/// <summary>
/// 识别完成事件参数
/// </summary>
public class RecognitionCompletedEventArgs : EventArgs
{
    /// <summary>
    /// 识别结果
    /// </summary>
    public RecognitionResult Result { get; set; } = null!;
}
