using IndustrialVoiceRecorder.Application.Services;
using IndustrialVoiceRecorder.Domain.Entities;
using IndustrialVoiceRecorder.Domain.ValueObjects;
using IndustrialVoiceRecorder.Infrastructure.SpeechRecognition;
using IndustrialVoiceRecorder.Shared;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace IndustrialVoiceRecorder.Infrastructure.Services;

/// <summary>
/// 语音处理协调服务 (v3.1)
/// 修复：嘈杂环境中语音开头丢失 — 改用环形预缓冲保留最近1秒音频
/// </summary>
public class VoiceProcessingService : IDisposable
{
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly ISpeechRecognitionService _speechRecognitionService;
    private readonly IRecordService _recordService;
    private readonly TextProcessor _textProcessor;
    private readonly ILogger<VoiceProcessingService> _logger;

    // 主缓冲区（识别用）
    private readonly List<byte> _audioBuffer = new();
    private readonly object _bufferLock = new();

    // ★ 环形预缓冲区：始终保留最近N字节的音频数据
    // 当检测到语音开始时，将这些数据全部加入主缓冲区
    // 这样即使VAD检测有延迟，语音开头也不会丢失
    private readonly Queue<byte[]> _ringBuffer = new();
    private int _ringBufferTotalBytes = 0;
    private readonly int _ringBufferMaxBytes;  // 从配置读取

    // VAD参数
    private readonly float _vadMultiplier;
    private readonly float _vadMinThreshold;
    private readonly int _silenceTimeoutMs;
    private readonly int _minAudioBytes;
    private readonly int _maxBufferBytes;
    private const float NoiseFloorAlpha = 0.01f; // 降低到0.01，让噪音基线更新更慢

    // 运行时状态
    private float _noiseFloor = 0.0001f;
    private bool _isSpeechActive;
    private DateTime _lastSpeechTime = DateTime.MinValue;
    private bool _hadSpeechInBuffer;
    private bool _isProcessing;
    private int _totalFrames;
    private int _speechFrames;

    // 序号
    private int _dailySeqNumber;
    private string _currentDateStr = "";

    public string CurrentOperator { get; set; } = "默认操作员";

    public event EventHandler<VoiceRecord>? RecordSaved;
    public event EventHandler<string>? RealtimeTextReceived;
    public event EventHandler<string>? StatusChanged;

    public VoiceProcessingService(
        IAudioCaptureService audioCaptureService,
        ISpeechRecognitionService speechRecognitionService,
        IRecordService recordService,
        TextProcessor textProcessor,
        ILogger<VoiceProcessingService> logger)
    {
        _audioCaptureService = audioCaptureService;
        _speechRecognitionService = speechRecognitionService;
        _recordService = recordService;
        _textProcessor = textProcessor;
        _logger = logger;

        _vadMultiplier = AppConfig.VadEnergyMultiplier;
        _vadMinThreshold = AppConfig.VadMinThreshold;
        _silenceTimeoutMs = AppConfig.VadSilenceTimeoutMs;
        _minAudioBytes = AppConfig.VadMinAudioBytes;
        _maxBufferBytes = AppConfig.VadMaxBufferBytes;

        // 环形预缓冲：保留最近1秒音频 = 16000Hz * 2bytes * 1s = 32000 bytes
        // 嘈杂环境需要更大的预缓冲来捕捉语音开头
        _ringBufferMaxBytes = AppConfig.VadPreBufferBytes * 2; // config默认16000*2=32000（1秒）

        _logger.LogInformation("VAD参数: 倍数={M}, 阈值={T}, 静音={S}ms, 预缓冲={P}bytes(环形)",
            _vadMultiplier, _vadMinThreshold, _silenceTimeoutMs, _ringBufferMaxBytes);

        _audioCaptureService.AudioDataAvailable += OnAudioDataAvailable;
    }

    public async Task InitializeAsync(string modelPath)
    {
        StatusChanged?.Invoke(this, "正在加载识别模型...");
        await _speechRecognitionService.InitializeAsync(modelPath);
        StatusChanged?.Invoke(this, "识别模型加载完成");
    }

    public void Start()
    {
        lock (_bufferLock)
        {
            _audioBuffer.Clear();
            _ringBuffer.Clear();
            _ringBufferTotalBytes = 0;
            _isSpeechActive = false;
            _hadSpeechInBuffer = false;
            _lastSpeechTime = DateTime.MinValue;
            _totalFrames = 0;
            _speechFrames = 0;
            _noiseFloor = 0.0001f;
        }
        _audioCaptureService.StartRecording();
        StatusChanged?.Invoke(this, "录音中，等待语音输入...");
    }

    public void Stop() => _ = StopAsync();

    public async Task StopAsync()
    {
        _audioCaptureService.StopRecording();
        await ProcessBufferedAudioAsync();
        StatusChanged?.Invoke(this, "已停止录音");
    }

    private string GenerateSerialNumber()
    {
        var today = DateTime.Now.ToString("yyyyMMdd");
        if (today != _currentDateStr) { _currentDateStr = today; _dailySeqNumber = 0; }
        _dailySeqNumber++;
        return $"REC-{today}-{_dailySeqNumber:D3}";
    }

    private string? SaveAudioToWav(byte[] pcmData, string serialNumber)
    {
        try
        {
            var audioDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                AppConfig.AudioSaveDirectory, DateTime.Now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(audioDir);
            var filePath = Path.Combine(audioDir, $"{serialNumber}_{DateTime.Now:HHmmss}.wav");
            var format = new WaveFormat(16000, 16, 1);
            using (var writer = new WaveFileWriter(filePath, format))
                writer.Write(pcmData, 0, pcmData.Length);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存音频文件失败");
            return null;
        }
    }

    /// <summary>
    /// 音频数据到达事件处理
    /// 核心改进：使用环形预缓冲区保留最近1秒音频
    /// 这样无论VAD检测有多少延迟，语音开头都不会丢失
    /// </summary>
    private async void OnAudioDataAvailable(object? sender, AudioDataAvailableEventArgs e)
    {
        try
        {
            var audioData = e.AudioData;
            float[] samples = audioData.ToFloatSamples();
            float energy = CalculateEnergy(samples);
            _totalFrames++;

            float vadThreshold = Math.Max(_noiseFloor * _vadMultiplier, _vadMinThreshold);
            bool isSpeech = energy > vadThreshold;

            if (_totalFrames % 100 == 0)
            {
                _logger.LogDebug("VAD: 帧={F}, 能量={E:F6}, 阈值={T:F6}, 基线={B:F6}, 缓冲={Buf}bytes, 环形={Ring}bytes",
                    _totalFrames, energy, vadThreshold, _noiseFloor, _audioBuffer.Count, _ringBufferTotalBytes);
            }

            bool shouldProcess = false;

            lock (_bufferLock)
            {
                if (isSpeech)
                {
                    _speechFrames++;

                    // ★ 语音刚开始：将环形预缓冲区中的所有数据加入主缓冲
                    // 这些是检测到语音前的最近1秒音频，包含了语音开头
                    if (!_isSpeechActive && _ringBuffer.Count > 0)
                    {
                        foreach (var chunk in _ringBuffer)
                            _audioBuffer.AddRange(chunk);
                        _logger.LogDebug("语音开始，注入环形预缓冲: {Size}bytes ({Count}帧)",
                            _ringBufferTotalBytes, _ringBuffer.Count);
                        _ringBuffer.Clear();
                        _ringBufferTotalBytes = 0;
                    }

                    _isSpeechActive = true;
                    _hadSpeechInBuffer = true;
                    _lastSpeechTime = DateTime.Now;
                    _audioBuffer.AddRange(audioData.Data);
                }
                else
                {
                    // 静音帧处理
                    if (_isSpeechActive)
                    {
                        // 语音活动期间的静音也加入（词间停顿）
                        _audioBuffer.AddRange(audioData.Data);
                    }
                    else
                    {
                        // ★ 非语音状态：将数据加入环形预缓冲区
                        _ringBuffer.Enqueue(audioData.Data.ToArray());
                        _ringBufferTotalBytes += audioData.Data.Length;

                        // 环形缓冲区超限，移除最旧的
                        while (_ringBufferTotalBytes > _ringBufferMaxBytes && _ringBuffer.Count > 0)
                        {
                            var old = _ringBuffer.Dequeue();
                            _ringBufferTotalBytes -= old.Length;
                        }

                        // 更新噪音基线（仅非语音且非语音活动时更新）
                        _noiseFloor = _noiseFloor * (1 - NoiseFloorAlpha) + energy * NoiseFloorAlpha;
                    }
                }

                // 触发条件1：语音结束（静音超时）
                if (_isSpeechActive && !isSpeech && _hadSpeechInBuffer &&
                    (DateTime.Now - _lastSpeechTime).TotalMilliseconds > _silenceTimeoutMs &&
                    _audioBuffer.Count > _minAudioBytes)
                {
                    shouldProcess = true;
                }

                // 触发条件2：缓冲区满（有语音）
                if (_audioBuffer.Count >= _maxBufferBytes && _hadSpeechInBuffer)
                    shouldProcess = true;

                // 条件3：无语音纯噪音满了，丢弃
                if (_audioBuffer.Count >= _maxBufferBytes && !_hadSpeechInBuffer)
                {
                    _audioBuffer.Clear();
                    _isSpeechActive = false;
                }
            }

            if (shouldProcess && !_isProcessing)
                await ProcessBufferedAudioAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理音频数据时出错");
        }
    }

    private async Task ProcessBufferedAudioAsync()
    {
        byte[] audioData;
        lock (_bufferLock)
        {
            if (_audioBuffer.Count < _minAudioBytes || !_hadSpeechInBuffer)
            {
                _audioBuffer.Clear();
                _isSpeechActive = false;
                _hadSpeechInBuffer = false;
                return;
            }
            audioData = _audioBuffer.ToArray();
            _audioBuffer.Clear();
            _isSpeechActive = false;
            _hadSpeechInBuffer = false;
            _isProcessing = true;
        }

        try
        {
            StatusChanged?.Invoke(this, "正在识别...");
            var audioDuration = (double)audioData.Length / 32000;
            var serialNumber = GenerateSerialNumber();
            var audioFilePath = SaveAudioToWav(audioData, serialNumber);

            var result = await _speechRecognitionService.RecognizeAsync(audioData);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Text))
            {
                StatusChanged?.Invoke(this, "录音中，等待语音输入...");
                return;
            }

            RealtimeTextReceived?.Invoke(this, result.Text);

            var processed = _textProcessor.Process(result.Text);
            string normalizedText = ChineseNumberConverter.Convert(processed.CorrectedText);

            var record = new VoiceRecord
            {
                SerialNumber = serialNumber,
                RecordTime = DateTime.Now,
                RecognizedText = processed.CorrectedText,
                NormalizedText = normalizedText,
                RawText = processed.OriginalText,
                AudioFilePath = audioFilePath,
                DeviceName = _audioCaptureService.CurrentDevice?.Name ?? "未知设备",
                Duration = audioDuration,
                Confidence = result.Confidence,
                Category = processed.Category,
                Status = processed.Status,
                ProductId = processed.ProductId,
                Operator = CurrentOperator,
                Remarks = processed.Remarks
            };

            var id = await _recordService.SaveRecordAsync(record);
            record.Id = id;
            RecordSaved?.Invoke(this, record);
            StatusChanged?.Invoke(this, "录音中，等待语音输入...");

            _logger.LogInformation("记录: {Serial} [{Status}] \"{Text}\"", serialNumber, record.Status, record.RecognizedText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理缓冲音频失败");
            StatusChanged?.Invoke(this, "识别出错...");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private float CalculateEnergy(float[] samples)
    {
        if (samples.Length == 0) return 0;
        float sum = 0;
        foreach (var s in samples) sum += s * s;
        return sum / samples.Length;
    }

    public void Dispose()
    {
        _audioCaptureService.AudioDataAvailable -= OnAudioDataAvailable;
    }
}
