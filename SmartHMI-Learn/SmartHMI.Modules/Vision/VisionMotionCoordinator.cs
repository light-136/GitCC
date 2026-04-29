using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using SmartHMI.Modules.Motion;

namespace SmartHMI.Modules.Vision;

/// <summary>
/// 视觉运动协同：视觉引导定位（Vision-Guided Motion）
/// 流程：触发视觉 → 获取偏移量 → 补偿运动轴
/// </summary>
public class VisionMotionCoordinator
{
    private readonly IVisionService _vision;
    private readonly MotionManager _motion;

    public VisionMotionCoordinator(IVisionService vision, MotionManager motion)
    {
        _vision = vision;
        _motion = motion;
    }

    /// <summary>
    /// 执行视觉引导定位：拍照 → 计算偏移 → 补偿 X/Y 轴
    /// </summary>
    public async Task<(bool Success, string Message)> AlignAsync(string jobName)
    {
        var result = await _vision.TriggerAsync(jobName);
        if (result.ResultType == VisionResultType.NG)
            return (false, $"视觉检测 NG：{result.DefectType}，置信度 {result.Confidence:P1}");

        if (result.ResultType == VisionResultType.Uncertain)
            return (false, $"视觉结果不确定，置信度 {result.Confidence:P1}，请重试");

        // 获取当前轴位置并补偿偏移
        if (_motion.Axes.TryGetValue("X", out var xAxis) && result.OffsetX != 0)
            xAxis.MoveToPosition(xAxis.Axis.Position + result.OffsetX);

        if (_motion.Axes.TryGetValue("Y", out var yAxis) && result.OffsetY != 0)
            yAxis.MoveToPosition(yAxis.Axis.Position + result.OffsetY);

        return (true, $"视觉引导完成，偏移补偿 X={result.OffsetX:F3}mm Y={result.OffsetY:F3}mm");
    }
}
