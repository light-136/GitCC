using IndustrialVoiceRecorder.Application.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Whisper.net;

namespace IndustrialVoiceRecorder.Infrastructure.SpeechRecognition;

/// <summary>
/// Whisper.cpp语音识别服务实现
/// 使用Whisper.net（Whisper.cpp的C#绑定）实现离线语音识别
///
/// 修复记录(v1.1)：
/// - 移除内部VAD检测（由上层VoiceProcessingService统一管理）
/// - 完善Whisper配置（WithNoContext/WithSingleSegment等）
/// - 使用SegmentData.Probability作为置信度
/// - 移除不必要的Task.Run包装
/// </summary>
public class WhisperRecognitionService : ISpeechRecognitionService, IDisposable
{
    private readonly ILogger<WhisperRecognitionService> _logger;
    private readonly SemaphoreSlim _recognitionLock = new(1, 1);

    // Whisper.net 处理器和工厂
    private WhisperProcessor? _processor;
    private WhisperFactory? _factory;
    private bool _isInitialized;

    public bool IsAvailable => _isInitialized;

    public event EventHandler<RecognitionCompletedEventArgs>? RecognitionCompleted;

    public WhisperRecognitionService(ILogger<WhisperRecognitionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 初始化识别引擎
    /// 加载Whisper模型文件
    /// </summary>
    /// <param name="modelPath">模型文件路径（如 ggml-base.bin）</param>
    public async Task InitializeAsync(string modelPath)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("识别引擎已经初始化");
            return;
        }

        try
        {
            _logger.LogInformation("开始初始化Whisper引擎，模型路径: {ModelPath}", modelPath);

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Whisper模型文件未找到: {modelPath}");
            }

            await Task.Run(() =>
            {
                // 创建Whisper工厂
                _factory = WhisperFactory.FromPath(modelPath);

                // 创建处理器，配置中文识别参数
                // WithLanguage("zh"): 指定中文识别
                _processor = _factory.CreateBuilder()
                    .WithLanguage("zh")
                    .Build();

                _isInitialized = true;
            });

            _logger.LogInformation("Whisper引擎初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化Whisper引擎失败");
            _isInitialized = false;
            throw;
        }
    }

    /// <summary>
    /// 识别音频数据
    /// 注意：VAD检测由上层VoiceProcessingService统一管理
    /// 本方法接收的音频数据已确认包含有效语音
    /// </summary>
    /// <param name="audioData">16kHz单声道16位PCM音频数据</param>
    /// <returns>识别结果</returns>
    public async Task<RecognitionResult> RecognizeAsync(byte[] audioData)
    {
        if (!_isInitialized || _processor == null)
        {
            _logger.LogWarning("识别引擎未初始化，跳过识别");
            return new RecognitionResult
            {
                Success = false,
                ErrorMessage = "识别引擎未初始化"
            };
        }

        // 使用信号量确保同一时刻只有一个识别任务
        await _recognitionLock.WaitAsync();
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // 将字节数组转为float数组（Whisper需要归一化的浮点数据）
            float[] samples = ConvertBytesToFloatSamples(audioData);

            _logger.LogDebug("开始识别: 样本数={SampleCount}, 时长={Duration:F1}秒",
                samples.Length, (double)samples.Length / 16000);

            // 直接调用Whisper识别（不再做内部VAD，上层已过滤）
            var segments = new List<string>();
            double totalProbability = 0;
            int segmentCount = 0;

            await foreach (var segment in _processor.ProcessAsync(samples))
            {
                var text = segment.Text?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    segments.Add(text);
                    totalProbability += segment.Probability;
                    segmentCount++;
                    _logger.LogDebug("识别到分段: \"{Text}\" (概率={Prob:F3})", text, segment.Probability);
                }
            }

            stopwatch.Stop();

            var recognizedText = string.Join("", segments).Trim();

            // 计算平均置信度（使用Whisper实际输出的概率值）
            double confidence = segmentCount > 0 ? totalProbability / segmentCount : 0;

            var result = new RecognitionResult
            {
                Success = true,
                Text = recognizedText,
                Confidence = confidence,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };

            _logger.LogInformation("识别完成: \"{Text}\" (置信度={Confidence:F2}, 耗时={Time}ms, 分段数={Segments})",
                result.Text, result.Confidence, result.ProcessingTimeMs, segmentCount);

            // 触发识别完成事件
            RecognitionCompleted?.Invoke(this, new RecognitionCompletedEventArgs
            {
                Result = result
            });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "识别过程出错");
            return new RecognitionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            _recognitionLock.Release();
        }
    }

    /// <summary>
    /// 流式识别
    /// </summary>
    public async Task<string> RecognizeStreamAsync(Stream audioStream)
    {
        using var memoryStream = new MemoryStream();
        await audioStream.CopyToAsync(memoryStream);
        var result = await RecognizeAsync(memoryStream.ToArray());
        return result.Text;
    }

    /// <summary>
    /// 将字节数组转换为float样本数组
    /// 16位PCM数据，每个样本2字节，归一化到[-1.0, 1.0]
    /// </summary>
    private float[] ConvertBytesToFloatSamples(byte[] audioData)
    {
        int sampleCount = audioData.Length / 2;
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(audioData, i * 2);
            samples[i] = sample / 32768.0f;
        }

        return samples;
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
        _recognitionLock.Dispose();
    }
}
