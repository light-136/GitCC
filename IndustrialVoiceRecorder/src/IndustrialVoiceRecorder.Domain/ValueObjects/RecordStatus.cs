namespace IndustrialVoiceRecorder.Domain.ValueObjects;

/// <summary>
/// 记录状态枚举
/// </summary>
public enum RecordStatus
{
    /// <summary>
    /// 合格
    /// </summary>
    OK,

    /// <summary>
    /// 不合格
    /// </summary>
    NG,

    /// <summary>
    /// 未知
    /// </summary>
    Unknown
}

/// <summary>
/// 记录状态扩展方法
/// </summary>
public static class RecordStatusExtensions
{
    /// <summary>
    /// 转换为字符串
    /// </summary>
    public static string ToDisplayString(this RecordStatus status)
    {
        return status switch
        {
            RecordStatus.OK => "合格",
            RecordStatus.NG => "不合格",
            _ => "未知"
        };
    }

    /// <summary>
    /// 从字符串解析
    /// </summary>
    public static RecordStatus FromString(string status)
    {
        return status?.ToUpper() switch
        {
            "OK" => RecordStatus.OK,
            "NG" => RecordStatus.NG,
            "合格" => RecordStatus.OK,
            "不合格" => RecordStatus.NG,
            _ => RecordStatus.Unknown
        };
    }
}
