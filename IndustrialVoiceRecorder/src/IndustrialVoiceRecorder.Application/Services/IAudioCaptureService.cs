using IndustrialVoiceRecorder.Domain.ValueObjects;

namespace IndustrialVoiceRecorder.Application.Services;

/// <summary>
/// 音频采集服务接口
/// 负责从音频设备采集音频数据
/// </summary>
public interface IAudioCaptureService
{
    /// <summary>
    /// 获取可用的音频设备列表
    /// </summary>
    /// <returns>设备信息列表</returns>
    List<AudioDeviceInfo> GetAvailableDevices();

    /// <summary>
    /// 选择音频设备
    /// </summary>
    /// <param name="deviceIndex">设备索引</param>
    void SelectDevice(int deviceIndex);

    /// <summary>
    /// 开始录音
    /// </summary>
    void StartRecording();

    /// <summary>
    /// 停止录音
    /// </summary>
    void StopRecording();

    /// <summary>
    /// 是否正在录音
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// 当前选中的设备
    /// </summary>
    AudioDeviceInfo? CurrentDevice { get; }

    /// <summary>
    /// 音频数据可用事件
    /// </summary>
    event EventHandler<AudioDataAvailableEventArgs>? AudioDataAvailable;

    /// <summary>
    /// 录音状态变化事件
    /// </summary>
    event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged;
}

/// <summary>
/// 音频设备信息
/// </summary>
public class AudioDeviceInfo
{
    /// <summary>
    /// 设备索引
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 设备名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 是否为蓝牙设备
    /// </summary>
    public bool IsBluetooth { get; set; }

    /// <summary>
    /// 是否为默认设备
    /// </summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// 音频数据可用事件参数
/// </summary>
public class AudioDataAvailableEventArgs : EventArgs
{
    /// <summary>
    /// 音频数据
    /// </summary>
    public AudioData AudioData { get; set; } = null!;
}

/// <summary>
/// 录音状态变化事件参数
/// </summary>
public class RecordingStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 是否正在录音
    /// </summary>
    public bool IsRecording { get; set; }

    /// <summary>
    /// 状态消息
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
