namespace IndustrialVoiceRecorder.Domain.ValueObjects;

/// <summary>
/// 音频数据值对象
/// 包含音频的原始数据和元信息
/// </summary>
public class AudioData
{
    /// <summary>
    /// 音频字节数据
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 采样率（Hz）
    /// </summary>
    public int SampleRate { get; set; }

    /// <summary>
    /// 声道数
    /// </summary>
    public int Channels { get; set; }

    /// <summary>
    /// 位深度
    /// </summary>
    public int BitsPerSample { get; set; }

    /// <summary>
    /// 时长（秒）
    /// </summary>
    public double Duration { get; set; }

    /// <summary>
    /// 转换为float样本数组（用于Whisper识别）
    /// </summary>
    public float[] ToFloatSamples()
    {
        int sampleCount = Data.Length / (BitsPerSample / 8);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(Data, i * 2);
            samples[i] = sample / 32768.0f; // 归一化到 [-1.0, 1.0]
        }

        return samples;
    }
}
