// ============================================================
// 文件：IVisionStep.cs
// 层级：硬件抽象层（Hardware Layer）> Vision > Pipeline
// 职责：定义视觉处理管线步骤的接口契约。
//       每个视觉步骤是管线中的一个原子操作单元，
//       接收上一步的 VisionContext，返回本步骤的执行结果。
//
// 设计思路：
//   管线（VisionPipeline）将多个 IVisionStep 串联执行，
//   前一步的输出 ImageData 作为下一步的输入，
//   每步可独立配置参数和处理逻辑，
//   便于灵活组合不同的图像处理算法。
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Models;

namespace SmartIndustry.Hardware.Vision.Pipeline
{
    /// <summary>
    /// 视觉处理上下文 — 在管线各步骤之间传递的共享数据容器。
    /// 包含当前图像（每步可能修改）、元数据和中间结果。
    /// </summary>
    public class VisionContext
    {
        /// <summary>当前处理的图像数据（步骤可替换此引用以输出处理后的图像）</summary>
        public ImageData CurrentImage { get; set; } = new();

        /// <summary>原始输入图像（管线开始时的输入，只读）</summary>
        public ImageData OriginalImage { get; set; } = new();

        /// <summary>各步骤存储的中间结果（Key=步骤名称）</summary>
        public Dictionary<string, object> StepResults { get; set; } = new();

        /// <summary>管线级参数（步骤可读取的全局配置，Key=参数名）</summary>
        public Dictionary<string, object> Parameters { get; set; } = new();

        /// <summary>是否发生了错误（任意步骤设置为 true 时，后续步骤可选择跳过）</summary>
        public bool HasError { get; set; }

        /// <summary>错误信息</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>管线执行开始时间</summary>
        public DateTime StartTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 视觉步骤执行结果 — 每个 IVisionStep 执行后返回的数据。
    /// </summary>
    public class VisionStepResult
    {
        /// <summary>步骤名称</summary>
        public string StepName { get; set; } = string.Empty;

        /// <summary>是否成功执行</summary>
        public bool IsSuccess { get; set; }

        /// <summary>此步骤输出的图像（成功时有效）</summary>
        public ImageData? OutputImage { get; set; }

        /// <summary>步骤执行耗时</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>失败原因（成功时为空）</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>步骤产生的附加数据（如检测结果、测量值等）</summary>
        public object? ResultData { get; set; }

        /// <summary>创建成功结果</summary>
        public static VisionStepResult Success(string name, ImageData output, TimeSpan duration, object? data = null)
            => new() { StepName = name, IsSuccess = true, OutputImage = output, Duration = duration, ResultData = data };

        /// <summary>创建失败结果</summary>
        public static VisionStepResult Failure(string name, string error, TimeSpan duration)
            => new() { StepName = name, IsSuccess = false, ErrorMessage = error, Duration = duration };
    }

    /// <summary>
    /// 视觉处理步骤接口。
    /// 每个实现类封装一个图像处理算法（预处理、检测、测量等），
    /// 通过管线链式执行实现复杂的视觉检测流程。
    ///
    /// 实现约定：
    ///   - ExecuteAsync 不应修改 context.OriginalImage（只读）
    ///   - 可修改 context.CurrentImage（输出处理后图像）
    ///   - 可向 context.StepResults[Name] 存储中间结果
    ///   - 捕获内部异常，通过 VisionStepResult.IsSuccess=false 返回，而非抛出
    ///   - 实现类应支持并发调用（同一实例可被多个管线并发使用）
    /// </summary>
    public interface IVisionStep
    {
        /// <summary>步骤名称（管线内唯一，用于日志和结果查询）</summary>
        string Name { get; }

        /// <summary>步骤描述（人可读的功能说明）</summary>
        string Description { get; }

        /// <summary>是否启用（false=跳过此步骤）</summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// 异步执行此视觉处理步骤。
        /// </summary>
        /// <param name="context">视觉上下文（包含当前图像和历史步骤结果）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>本步骤的执行结果</returns>
        Task<VisionStepResult> ExecuteAsync(VisionContext context, CancellationToken cancellationToken = default);
    }
}
