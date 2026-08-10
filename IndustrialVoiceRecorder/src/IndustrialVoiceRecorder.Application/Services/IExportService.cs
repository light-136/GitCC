using IndustrialVoiceRecorder.Domain.Entities;

namespace IndustrialVoiceRecorder.Application.Services;

/// <summary>
/// 导出服务接口
/// 负责数据导出功能
/// </summary>
public interface IExportService
{
    /// <summary>
    /// 导出为Excel
    /// </summary>
    /// <param name="records">记录列表</param>
    /// <param name="filePath">文件路径</param>
    /// <returns>导出的文件路径</returns>
    Task<string> ExportToExcelAsync(List<VoiceRecord> records, string filePath);

    /// <summary>
    /// 导出为CSV
    /// </summary>
    /// <param name="records">记录列表</param>
    /// <param name="filePath">文件路径</param>
    /// <returns>导出的文件路径</returns>
    Task<string> ExportToCsvAsync(List<VoiceRecord> records, string filePath);

    /// <summary>
    /// 导出统计报表
    /// </summary>
    /// <param name="stats">统计数据</param>
    /// <param name="filePath">文件路径</param>
    /// <returns>导出的文件路径</returns>
    Task<string> ExportStatisticsAsync(Statistics stats, string filePath);

    /// <summary>
    /// 导出进度事件
    /// </summary>
    event EventHandler<ExportProgressEventArgs>? ExportProgress;
}

/// <summary>
/// 导出进度事件参数
/// </summary>
public class ExportProgressEventArgs : EventArgs
{
    /// <summary>
    /// 进度百分比（0-100）
    /// </summary>
    public int ProgressPercentage { get; set; }

    /// <summary>
    /// 状态消息
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
