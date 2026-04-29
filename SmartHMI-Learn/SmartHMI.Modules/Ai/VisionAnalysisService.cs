using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Modules.Ai;

/// <summary>
/// AI 视觉分析扩展服务
/// 在 VisionService 基础上增加：缺陷分类、趋势分析、质量预测
/// 可替换为 ONNX Runtime 真实推理模型
/// </summary>
public class VisionAnalysisService
{
    private readonly IVisionService _vision;
    private readonly List<VisionResult> _analysisBuffer = new();
    private readonly Lock _lock = new();

    public VisionAnalysisService(IVisionService vision)
    {
        _vision = vision;
        _vision.ResultAvailable += OnResultAvailable;
    }

    private void OnResultAvailable(object? sender, VisionResult result)
    {
        lock (_lock)
        {
            _analysisBuffer.Add(result);
            if (_analysisBuffer.Count > 1000) _analysisBuffer.RemoveAt(0);
        }
    }

    /// <summary>获取最近 N 条结果的统计摘要</summary>
    public VisionStatsSummary GetStats(int recentCount = 100)
    {
        List<VisionResult> data;
        lock (_lock) data = _analysisBuffer.TakeLast(recentCount).ToList();

        if (data.Count == 0) return new VisionStatsSummary();

        var okCount = data.Count(r => r.ResultType == VisionResultType.OK);
        var ngCount = data.Count(r => r.ResultType == VisionResultType.NG);
        var uncertainCount = data.Count(r => r.ResultType == VisionResultType.Uncertain);

        var defectGroups = data
            .Where(r => r.ResultType == VisionResultType.NG && !string.IsNullOrEmpty(r.DefectType))
            .GroupBy(r => r.DefectType)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2)
            .ToList();

        return new VisionStatsSummary
        {
            TotalCount = data.Count,
            OkCount = okCount,
            NgCount = ngCount,
            UncertainCount = uncertainCount,
            YieldRate = data.Count > 0 ? (double)okCount / data.Count * 100 : 0,
            AvgConfidence = data.Average(r => r.Confidence),
            TopDefects = defectGroups,
            AnalyzedAt = DateTime.Now
        };
    }

    /// <summary>预测下一批次良率（基于近期趋势）</summary>
    public double PredictYield(int windowSize = 50)
    {
        List<VisionResult> data;
        lock (_lock) data = _analysisBuffer.TakeLast(windowSize).ToList();
        if (data.Count < 10) return 95.0; // 数据不足时返回默认值

        var recentYield = (double)data.Count(r => r.ResultType == VisionResultType.OK) / data.Count * 100;
        // 简单线性外推（实际可替换为 ML 模型）
        return Math.Round(recentYield * 0.98 + 95.0 * 0.02, 1);
    }
}

public class VisionStatsSummary
{
    public int TotalCount { get; set; }
    public int OkCount { get; set; }
    public int NgCount { get; set; }
    public int UncertainCount { get; set; }
    public double YieldRate { get; set; }
    public double AvgConfidence { get; set; }
    public List<(string DefectType, int Count)> TopDefects { get; set; } = new();
    public DateTime AnalyzedAt { get; set; }
}
