namespace IndustrialVoiceRecorder.Domain.Entities;

/// <summary>
/// 语音记录实体 (v3.0)
/// 新增：序号SerialNumber、音频保存AudioFilePath
/// </summary>
public class VoiceRecord
{
    public int Id { get; set; }

    /// <summary>
    /// 记录序号（如 REC-20260704-001）
    /// </summary>
    public string? SerialNumber { get; set; }

    public DateTime RecordTime { get; set; }

    /// <summary>
    /// 识别原文（不可修改，永久保留）
    /// </summary>
    public string RecognizedText { get; set; } = string.Empty;

    /// <summary>
    /// 数字化文本（汉字数字→阿拉伯数字）
    /// </summary>
    public string? NormalizedText { get; set; }

    /// <summary>
    /// 原始未处理文本
    /// </summary>
    public string? RawText { get; set; }

    /// <summary>
    /// 复核修改后的文本
    /// </summary>
    public string? CorrectedText { get; set; }

    /// <summary>
    /// 复核人
    /// </summary>
    public string? CorrectedBy { get; set; }

    /// <summary>
    /// 复核时间
    /// </summary>
    public DateTime? CorrectedAt { get; set; }

    /// <summary>
    /// 是否已复核
    /// </summary>
    public bool IsReviewed { get; set; }

    /// <summary>
    /// 原始音频文件路径（WAV格式，按日期归档）
    /// </summary>
    public string? AudioFilePath { get; set; }

    public string? DeviceName { get; set; }
    public double Duration { get; set; }
    public double Confidence { get; set; }
    public string? Category { get; set; }
    public string? Status { get; set; }
    public string? ProductId { get; set; }

    /// <summary>
    /// 录音操作员
    /// </summary>
    public string? Operator { get; set; }

    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 获取最终显示文本
    /// </summary>
    public string DisplayText => CorrectedText ?? NormalizedText ?? RecognizedText;
}
