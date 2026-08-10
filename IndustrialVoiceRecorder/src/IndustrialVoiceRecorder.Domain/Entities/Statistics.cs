namespace IndustrialVoiceRecorder.Domain.Entities;

/// <summary>
/// 统计数据实体
/// 用于存储每日的统计信息
/// </summary>
public class Statistics
{
    /// <summary>
    /// 统计ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 统计日期
    /// </summary>
    public DateTime StatDate { get; set; }

    /// <summary>
    /// 总记录数
    /// </summary>
    public int TotalRecords { get; set; }

    /// <summary>
    /// 合格数量
    /// </summary>
    public int OkCount { get; set; }

    /// <summary>
    /// 不合格数量
    /// </summary>
    public int NgCount { get; set; }

    /// <summary>
    /// 合格率（百分比）
    /// </summary>
    public double PassRate { get; set; }

    /// <summary>
    /// 分类统计（JSON格式）
    /// </summary>
    public string? CategoryStats { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
