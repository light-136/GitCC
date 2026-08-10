using IndustrialVoiceRecorder.Application.Services;
using IndustrialVoiceRecorder.Shared;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IndustrialVoiceRecorder.Infrastructure.SpeechRecognition;

/// <summary>
/// FunASR 语音识别服务实现 (v3.0 对接现有asr_server.py)
///
/// 对接接口：POST http://127.0.0.1:10095/asr
/// 请求格式：multipart/form-data, 字段名"audio"
/// 响应格式：纯文本字符串（识别结果）
///
/// 对应Python端代码：
///   audio_bin = request.files["audio"].read()
///   result = model.generate(input=audio_bin)
///   return json.dumps(result[0]["text"])
/// </summary>
public class FunAsrRecognitionService : ISpeechRecognitionService, IDisposable
{
    private readonly ILogger<FunAsrRecognitionService> _logger;
    private readonly SemaphoreSlim _recognitionLock = new(1, 1);

    private readonly HttpClient _httpClient;
    private bool _isInitialized;

    public bool IsAvailable => _isInitialized;
    public event EventHandler<RecognitionCompletedEventArgs>? RecognitionCompleted;

    public FunAsrRecognitionService(ILogger<FunAsrRecognitionService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(AppConfig.FunAsrUrl),
            Timeout = TimeSpan.FromSeconds(AppConfig.FunAsrTimeoutSeconds)
        };
    }

    /// <summary>
    /// 初始化：检查FunASR服务是否在线
    /// 通过向 /asr 发送一段极短的静音来验证服务可用性
    /// </summary>
    public async Task InitializeAsync(string modelPath)
    {
        if (_isInitialized) return;

        try
        {
            _logger.LogInformation("正在连接FunASR服务 {Url}...", AppConfig.FunAsrUrl);

            // 发送一小段静音音频来验证服务是否正常
            // 16kHz * 0.5秒 * 2字节 = 16000字节的静音
            var silenceData = new byte[16000];
            using var testContent = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(silenceData);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            testContent.Add(audioContent, "audio", "test.pcm");

            var response = await _httpClient.PostAsync(AppConfig.FunAsrEndpoint, testContent);
            var body = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("FunASR连接测试: HTTP {StatusCode}, 响应={Body}",
                (int)response.StatusCode, body);

            // 只要服务响应了（即使是空文本），就算连接成功
            _isInitialized = true;
            _logger.LogInformation("✅ FunASR服务连接成功！(Paraformer-zh 中文识别引擎)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接FunASR服务失败");
            _isInitialized = false;
            throw new InvalidOperationException(
                $"无法连接FunASR服务({AppConfig.FunAsrUrl})。\n" +
                "请先运行: python funasr\\asr_server.py", ex);
        }
    }

    /// <summary>
    /// 识别音频数据
    /// 通过multipart/form-data上传PCM音频到 /asr 接口
    /// </summary>
    public async Task<RecognitionResult> RecognizeAsync(byte[] audioData)
    {
        if (!_isInitialized)
        {
            return new RecognitionResult
            {
                Success = false,
                ErrorMessage = "FunASR服务未连接"
            };
        }

        await _recognitionLock.WaitAsync();
        try
        {
            var stopwatch = Stopwatch.StartNew();
            double duration = (double)audioData.Length / 32000;

            _logger.LogDebug("发送音频到FunASR: {Size}bytes, 时长={Duration:F1}秒",
                audioData.Length, duration);

            // 构造multipart请求（对接asr_server.py的 request.files["audio"]）
            using var formContent = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            formContent.Add(audioContent, "audio", "audio.pcm");

            // 发送POST请求
            var response = await _httpClient.PostAsync(AppConfig.FunAsrEndpoint, formContent);
            var responseBody = await response.Content.ReadAsStringAsync();

            stopwatch.Stop();

            _logger.LogDebug("FunASR响应: HTTP {StatusCode}, Body=\"{Body}\"",
                (int)response.StatusCode, responseBody);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("FunASR HTTP错误: {StatusCode} {Body}",
                    response.StatusCode, responseBody);
                return new RecognitionResult
                {
                    Success = false,
                    ErrorMessage = $"HTTP {(int)response.StatusCode}"
                };
            }

            // 解析响应
            // asr_server.py 返回: json.dumps(result[0]["text"]) → 带引号的字符串
            // 例如: "你好世界" （是一个JSON字符串）
            string recognizedText = "";

            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                // 尝试去掉JSON字符串的引号
                try
                {
                    recognizedText = JsonSerializer.Deserialize<string>(responseBody) ?? "";
                }
                catch
                {
                    // 如果解析失败，直接用原始文本（去掉首尾引号）
                    recognizedText = responseBody.Trim().Trim('"');
                }
            }

            var result = new RecognitionResult
            {
                Success = true,
                Text = recognizedText.Trim(),
                Confidence = 0.95,  // Paraformer-zh 默认置信度
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            };

            if (!string.IsNullOrEmpty(result.Text))
            {
                _logger.LogInformation("✅ FunASR识别: \"{Text}\" (耗时={Time}ms)",
                    result.Text, result.ProcessingTimeMs);
            }

            RecognitionCompleted?.Invoke(this, new RecognitionCompletedEventArgs { Result = result });
            return result;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("FunASR识别超时（>30秒）");
            return new RecognitionResult { Success = false, ErrorMessage = "识别超时" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "FunASR通信错误");
            _isInitialized = false;
            return new RecognitionResult { Success = false, ErrorMessage = $"通信错误: {ex.Message}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FunASR识别出错");
            return new RecognitionResult { Success = false, ErrorMessage = ex.Message };
        }
        finally
        {
            _recognitionLock.Release();
        }
    }

    public async Task<string> RecognizeStreamAsync(Stream audioStream)
    {
        using var ms = new MemoryStream();
        await audioStream.CopyToAsync(ms);
        var result = await RecognizeAsync(ms.ToArray());
        return result.Text;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _recognitionLock.Dispose();
    }
}
