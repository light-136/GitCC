using IndustrialVoiceRecorder.Domain.Entities;

namespace IndustrialVoiceRecorder.Application.Services;

/// <summary>
/// 记录服务接口
/// 负责语音记录的业务逻辑处理
/// </summary>
public interface IRecordService
{
    /// <summary>
    /// 保存语音记录
    /// </summary>
    /// <param name="record">语音记录实体</param>
    /// <returns>记录ID</returns>
    Task<int> SaveRecordAsync(VoiceRecord record);

    /// <summary>
    /// 获取记录列表
    /// </summary>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <returns>记录列表</returns>
    Task<List<VoiceRecord>> GetRecordsAsync(DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// 根据ID获取记录
    /// </summary>
    /// <param name="recordId">记录ID</param>
    /// <returns>语音记录</returns>
    Task<VoiceRecord?> GetRecordByIdAsync(int recordId);

    /// <summary>
    /// 更新记录
    /// </summary>
    /// <param name="record">语音记录实体</param>
    Task UpdateRecordAsync(VoiceRecord record);

    /// <summary>
    /// 删除记录
    /// </summary>
    /// <param name="recordId">记录ID</param>
    Task DeleteRecordAsync(int recordId);

    /// <summary>
    /// 获取统计数据
    /// </summary>
    /// <param name="date">统计日期</param>
    /// <returns>统计数据</returns>
    Task<Statistics> GetStatisticsAsync(DateTime date);

    /// <summary>
    /// 搜索记录
    /// </summary>
    /// <param name="keyword">关键词</param>
    /// <returns>记录列表</returns>
    Task<List<VoiceRecord>> SearchRecordsAsync(string keyword);
}
