using IndustrialVoiceRecorder.Application.Services;
using IndustrialVoiceRecorder.Domain.Entities;
using IndustrialVoiceRecorder.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace IndustrialVoiceRecorder.Infrastructure.Services;

/// <summary>
/// 记录服务实现
/// 实现业务逻辑层的记录管理功能
/// </summary>
public class RecordService : IRecordService
{
    private readonly IVoiceRecordRepository _repository;
    private readonly ILogger<RecordService> _logger;

    public RecordService(IVoiceRecordRepository repository, ILogger<RecordService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<int> SaveRecordAsync(VoiceRecord record)
    {
        try
        {
            // 设置默认值
            if (record.RecordTime == default)
                record.RecordTime = DateTime.Now;

            if (record.CreatedAt == default)
                record.CreatedAt = DateTime.Now;

            record.UpdatedAt = DateTime.Now;

            var id = await _repository.InsertAsync(record);
            _logger.LogInformation("保存记录成功，ID: {Id}, 文本: {Text}", id, record.RecognizedText);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存记录失败");
            throw;
        }
    }

    public async Task<List<VoiceRecord>> GetRecordsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            if (startDate.HasValue && endDate.HasValue)
            {
                return await _repository.GetByDateRangeAsync(startDate.Value, endDate.Value);
            }
            else
            {
                return await _repository.GetAllAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取记录列表失败");
            throw;
        }
    }

    public async Task<VoiceRecord?> GetRecordByIdAsync(int recordId)
    {
        try
        {
            return await _repository.GetByIdAsync(recordId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取记录失败，ID: {Id}", recordId);
            throw;
        }
    }

    public async Task UpdateRecordAsync(VoiceRecord record)
    {
        try
        {
            record.UpdatedAt = DateTime.Now;
            await _repository.UpdateAsync(record);
            _logger.LogInformation("更新记录成功，ID: {Id}", record.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新记录失败");
            throw;
        }
    }

    public async Task DeleteRecordAsync(int recordId)
    {
        try
        {
            await _repository.DeleteAsync(recordId);
            _logger.LogInformation("删除记录成功，ID: {Id}", recordId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除记录失败");
            throw;
        }
    }

    public async Task<Statistics> GetStatisticsAsync(DateTime date)
    {
        try
        {
            return await _repository.GetStatisticsAsync(date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取统计数据失败");
            throw;
        }
    }

    public async Task<List<VoiceRecord>> SearchRecordsAsync(string keyword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return await _repository.GetAllAsync();
            }

            return await _repository.SearchAsync(keyword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "搜索记录失败");
            throw;
        }
    }
}
