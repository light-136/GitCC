namespace IndustrialVoiceRecorder.Domain.Entities;

/// <summary>
/// 操作员实体
/// </summary>
public class OperatorInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? WorkId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 显示名称（工号-姓名 或 姓名）
    /// 注意：需要set以兼容WPF ComboBox的TwoWay绑定
    /// </summary>
    public string DisplayName
    {
        get => string.IsNullOrEmpty(WorkId) ? Name : $"{WorkId}-{Name}";
        set { /* WPF绑定需要setter，实际不做处理 */ }
    }
}
