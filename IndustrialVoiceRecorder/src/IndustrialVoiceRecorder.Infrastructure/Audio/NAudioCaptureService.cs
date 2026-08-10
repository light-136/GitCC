using IndustrialVoiceRecorder.Application.Services;
using IndustrialVoiceRecorder.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace IndustrialVoiceRecorder.Infrastructure.Audio;

/// <summary>
/// NAudio音频采集服务实现
/// 基于NAudio库实现音频设备的枚举和实时音频采集
/// </summary>
public class NAudioCaptureService : IAudioCaptureService
{
    private readonly ILogger<NAudioCaptureService> _logger;
    private WaveInEvent? _waveIn;
    private int _selectedDeviceIndex = -1;
    private AudioDeviceInfo? _currentDevice;

    // 音频参数配置
    private const int SampleRate = 16000;  // 16kHz 适合语音识别
    private const int Channels = 1;         // 单声道
    private const int BitsPerSample = 16;   // 16位
    private const int BufferMilliseconds = 100;  // 100ms缓冲

    public bool IsRecording { get; private set; }
    public AudioDeviceInfo? CurrentDevice => _currentDevice;

    public event EventHandler<AudioDataAvailableEventArgs>? AudioDataAvailable;
    public event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged;

    public NAudioCaptureService(ILogger<NAudioCaptureService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取可用的音频设备列表
    /// </summary>
    public List<AudioDeviceInfo> GetAvailableDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            int deviceCount = WaveInEvent.DeviceCount;
            _logger.LogInformation("检测到 {Count} 个音频输入设备", deviceCount);

            for (int i = 0; i < deviceCount; i++)
            {
                var capabilities = WaveInEvent.GetCapabilities(i);
                var deviceInfo = new AudioDeviceInfo
                {
                    Index = i,
                    Name = capabilities.ProductName,
                    // 判断是否为蓝牙设备
                    IsBluetooth = capabilities.ProductName.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
                                  capabilities.ProductName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                                  capabilities.ProductName.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                                  capabilities.ProductName.Contains("耳机", StringComparison.OrdinalIgnoreCase),
                    IsDefault = i == 0
                };

                // 过滤虚拟音频设备（ToDesk/向日葵等远程桌面软件的虚拟麦克风）
                bool isVirtual = capabilities.ProductName.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                                 capabilities.ProductName.Contains("虚拟", StringComparison.OrdinalIgnoreCase);

                if (isVirtual)
                {
                    _logger.LogDebug("跳过虚拟设备: {Name}", deviceInfo.Name);
                    continue;
                }

                devices.Add(deviceInfo);
                _logger.LogDebug("设备 {Index}: {Name} (蓝牙: {IsBluetooth})",
                    i, deviceInfo.Name, deviceInfo.IsBluetooth);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取音频设备列表失败");
        }

        return devices;
    }

    /// <summary>
    /// 选择音频设备
    /// </summary>
    public void SelectDevice(int deviceIndex)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("正在录音时无法切换设备，请先停止录音");
        }

        var devices = GetAvailableDevices();
        if (deviceIndex < 0 || deviceIndex >= devices.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceIndex), "设备索引超出范围");
        }

        _selectedDeviceIndex = deviceIndex;
        _currentDevice = devices[deviceIndex];
        _logger.LogInformation("已选择设备: {DeviceName}", _currentDevice.Name);
    }

    /// <summary>
    /// 开始录音
    /// </summary>
    public void StartRecording()
    {
        if (IsRecording)
        {
            _logger.LogWarning("已经在录音中");
            return;
        }

        if (_selectedDeviceIndex < 0)
        {
            // 如果没有选择设备，自动选择第一个设备
            var devices = GetAvailableDevices();
            if (devices.Count == 0)
            {
                throw new InvalidOperationException("没有可用的音频设备");
            }
            SelectDevice(0);
        }

        try
        {
            // 创建WaveInEvent实例
            _waveIn = new WaveInEvent
            {
                DeviceNumber = _selectedDeviceIndex,
                WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
                BufferMilliseconds = BufferMilliseconds
            };

            // 订阅事件
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            // 开始录音
            _waveIn.StartRecording();
            IsRecording = true;

            _logger.LogInformation("开始录音: 设备={Device}, 采样率={SampleRate}Hz, 声道={Channels}",
                _currentDevice?.Name, SampleRate, Channels);

            // 触发状态变化事件
            RecordingStateChanged?.Invoke(this, new RecordingStateChangedEventArgs
            {
                IsRecording = true,
                Message = "开始录音"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动录音失败");
            IsRecording = false;
            throw;
        }
    }

    /// <summary>
    /// 停止录音
    /// </summary>
    public void StopRecording()
    {
        if (!IsRecording)
        {
            _logger.LogWarning("当前未在录音");
            return;
        }

        try
        {
            _waveIn?.StopRecording();
            IsRecording = false;

            _logger.LogInformation("停止录音");

            // 触发状态变化事件
            RecordingStateChanged?.Invoke(this, new RecordingStateChangedEventArgs
            {
                IsRecording = false,
                Message = "停止录音"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止录音失败");
            throw;
        }
    }

    /// <summary>
    /// 音频数据可用事件处理
    /// </summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0)
            return;

        try
        {
            // 创建音频数据对象
            var audioData = new AudioData
            {
                Data = e.Buffer.Take(e.BytesRecorded).ToArray(),
                SampleRate = SampleRate,
                Channels = Channels,
                BitsPerSample = BitsPerSample,
                Duration = (double)e.BytesRecorded / (SampleRate * Channels * (BitsPerSample / 8))
            };

            // 触发音频数据可用事件
            AudioDataAvailable?.Invoke(this, new AudioDataAvailableEventArgs
            {
                AudioData = audioData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理音频数据失败");
        }
    }

    /// <summary>
    /// 录音停止事件处理
    /// </summary>
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsRecording = false;

        if (e.Exception != null)
        {
            _logger.LogError(e.Exception, "录音异常停止");
        }

        // 清理资源
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        StopRecording();
        _waveIn?.Dispose();
    }
}
