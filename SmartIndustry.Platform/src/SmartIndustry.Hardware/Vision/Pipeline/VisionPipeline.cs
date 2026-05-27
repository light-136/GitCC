// ============================================================
// 文件：VisionPipeline.cs
// 层级：硬件抽象层（Hardware Layer）> Vision > Pipeline
// 职责：视觉处理管线。
//       将多个 IVisionStep 按顺序串联，前一步的输出作为下一步的输入，
//       实现复杂的多步骤视觉处理流程（预处理→检测→测量→后处理）。
//
// 核心功能：
//   1. 步骤链式执行（List<IVisionStep>，顺序执行）
//   2. 每步输入上一步的 VisionContext（共享上下文传递）
//   3. 管线配置可 JSON 序列化（步骤名称列表+参数）
//   4. 执行全程计时（管线总时间和每步时间）
//   5. 单步异常处理：失败步骤记录错误，后续步骤默认继续（可配置停止）
//   6. 执行统计：成功步骤数、失败步骤数、总耗时
//
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartIndustry.Domain.Models;

namespace SmartIndustry.Hardware.Vision.Pipeline
{
    /// <summary>
    /// 管线执行结果 — 包含所有步骤的执行情况和总体统计。
    /// </summary>
    public class PipelineExecutionResult
    {
        /// <summary>管线名称</summary>
        public string PipelineName { get; set; } = string.Empty;

        /// <summary>管线是否整体成功（所有启用的步骤都成功）</summary>
        public bool IsSuccess { get; set; }

        /// <summary>最终输出图像</summary>
        public ImageData? OutputImage { get; set; }

        /// <summary>各步骤执行结果列表（按执行顺序）</summary>
        public List<VisionStepResult> StepResults { get; set; } = new();

        /// <summary>管线总执行时间</summary>
        public TimeSpan TotalDuration { get; set; }

        /// <summary>成功执行的步骤数</summary>
        public int SuccessStepCount { get; set; }

        /// <summary>失败的步骤数</summary>
        public int FailedStepCount { get; set; }

        /// <summary>跳过的步骤数（IsEnabled=false）</summary>
        public int SkippedStepCount { get; set; }

        /// <summary>执行时间戳</summary>
        public DateTime ExecutedAt { get; set; } = DateTime.Now;

        /// <summary>共享上下文（包含各步骤存储的中间结果）</summary>
        public VisionContext? Context { get; set; }
    }

    /// <summary>
    /// 管线配置 — 可序列化为 JSON 保存/加载管线定义。
    /// </summary>
    public class PipelineConfig
    {
        /// <summary>管线名称</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>管线描述</summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>步骤名称列表（按执行顺序）</summary>
        [JsonPropertyName("steps")]
        public List<string> StepNames { get; set; } = new();

        /// <summary>管线级参数</summary>
        [JsonPropertyName("parameters")]
        public Dictionary<string, string> Parameters { get; set; } = new();

        /// <summary>是否在步骤失败时立即停止整个管线</summary>
        [JsonPropertyName("stopOnFailure")]
        public bool StopOnFailure { get; set; } = false;

        /// <summary>版本号（配置变更时递增）</summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;
    }

    /// <summary>
    /// 视觉处理管线。
    /// 将多个 IVisionStep 串联执行，实现复杂的视觉检测流程。
    ///
    /// 使用示例：
    ///   var pipeline = new VisionPipeline("缺陷检测管线");
    ///   pipeline.AddStep(new GrayscaleStep());
    ///   pipeline.AddStep(new GaussianBlurStep());
    ///   pipeline.AddStep(new DefectDetectStep(engine));
    ///   var result = await pipeline.ExecuteAsync(inputImage);
    ///   Console.WriteLine($"管线耗时：{result.TotalDuration.TotalMilliseconds:F0}ms");
    ///   Console.WriteLine($"成功步骤：{result.SuccessStepCount}/{result.StepResults.Count}");
    /// </summary>
    public class VisionPipeline
    {
        // ==================== 私有字段 ====================

        /// <summary>步骤列表（按执行顺序）</summary>
        private readonly List<IVisionStep> _steps = new();

        /// <summary>管线锁（保护步骤列表的读写）</summary>
        private readonly object _pipelineLock = new();

        // ==================== 构造函数 ====================

        /// <summary>
        /// 构造视觉处理管线
        /// </summary>
        /// <param name="name">管线名称</param>
        /// <param name="stopOnFailure">步骤失败时是否停止管线（默认不停止，记录错误继续执行）</param>
        public VisionPipeline(string name = "DefaultPipeline", bool stopOnFailure = false)
        {
            Name = name;
            StopOnFailure = stopOnFailure;
        }

        // ==================== 公开属性 ====================

        /// <summary>管线名称</summary>
        public string Name { get; set; }

        /// <summary>步骤失败时是否立即停止管线</summary>
        public bool StopOnFailure { get; set; }

        /// <summary>当前步骤数</summary>
        public int StepCount { get { lock (_pipelineLock) return _steps.Count; } }

        // ==================== 步骤管理 ====================

        /// <summary>
        /// 在管线末尾添加步骤
        /// </summary>
        public VisionPipeline AddStep(IVisionStep step)
        {
            lock (_pipelineLock) { _steps.Add(step); }
            return this; // 支持链式调用
        }

        /// <summary>
        /// 在指定位置插入步骤
        /// </summary>
        public void InsertStep(int index, IVisionStep step)
        {
            lock (_pipelineLock) { _steps.Insert(index, step); }
        }

        /// <summary>
        /// 移除指定名称的步骤（移除第一个匹配项）
        /// </summary>
        public bool RemoveStep(string stepName)
        {
            lock (_pipelineLock)
            {
                var step = _steps.FirstOrDefault(s => s.Name == stepName);
                if (step == null) return false;
                _steps.Remove(step);
                return true;
            }
        }

        /// <summary>
        /// 清空所有步骤
        /// </summary>
        public void Clear()
        {
            lock (_pipelineLock) { _steps.Clear(); }
        }

        /// <summary>
        /// 获取步骤列表快照（线程安全的副本）
        /// </summary>
        public IReadOnlyList<IVisionStep> GetSteps()
        {
            lock (_pipelineLock) { return _steps.ToList(); }
        }

        // ==================== 执行管线 ====================

        /// <summary>
        /// 异步执行管线，返回完整的执行结果（含所有步骤结果）。
        /// </summary>
        /// <param name="inputImage">管线输入图像</param>
        /// <param name="parameters">管线级参数（传入各步骤的共享参数）</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async Task<PipelineExecutionResult> ExecuteAsync(
            ImageData inputImage,
            Dictionary<string, object>? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var pipelineSw = Stopwatch.StartNew();
            var pipelineResult = new PipelineExecutionResult { PipelineName = Name };

            // 初始化视觉上下文
            var context = new VisionContext
            {
                OriginalImage = inputImage,
                CurrentImage = inputImage.Clone(), // 工作图像=原始图像的副本
                Parameters = parameters ?? new(),
                StartTime = DateTime.Now
            };

            List<IVisionStep> stepsCopy;
            lock (_pipelineLock) { stepsCopy = _steps.ToList(); }

            // 逐步执行
            foreach (var step in stepsCopy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 跳过禁用的步骤
                if (!step.IsEnabled)
                {
                    pipelineResult.SkippedStepCount++;
                    continue;
                }

                VisionStepResult stepResult;
                try
                {
                    stepResult = await step.ExecuteAsync(context, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // 捕获步骤内部未处理异常，包装为失败结果
                    var dur = TimeSpan.Zero; // 异常时耗时不可知，置0
                    stepResult = VisionStepResult.Failure(step.Name, $"未处理异常：{ex.Message}", dur);
                }

                pipelineResult.StepResults.Add(stepResult);

                if (stepResult.IsSuccess)
                {
                    pipelineResult.SuccessStepCount++;
                    // 用本步骤输出的图像更新上下文的当前图像
                    if (stepResult.OutputImage != null)
                        context.CurrentImage = stepResult.OutputImage;
                    // 存储步骤结果到上下文
                    if (stepResult.ResultData != null)
                        context.StepResults[step.Name] = stepResult.ResultData;
                }
                else
                {
                    pipelineResult.FailedStepCount++;
                    context.HasError = true;
                    context.ErrorMessage = stepResult.ErrorMessage;

                    if (StopOnFailure)
                    {
                        // 标记后续步骤为跳过
                        pipelineResult.SkippedStepCount += stepsCopy.Count
                            - pipelineResult.StepResults.Count - pipelineResult.SkippedStepCount;
                        break;
                    }
                }
            }

            pipelineSw.Stop();

            pipelineResult.IsSuccess = pipelineResult.FailedStepCount == 0;
            pipelineResult.OutputImage = context.CurrentImage;
            pipelineResult.TotalDuration = pipelineSw.Elapsed;
            pipelineResult.Context = context;

            return pipelineResult;
        }

        // ==================== 序列化/反序列化 ====================

        /// <summary>
        /// 将管线配置（步骤名称列表+参数）序列化为 JSON 字符串
        /// </summary>
        public string ToJson()
        {
            var config = new PipelineConfig
            {
                Name = Name,
                StopOnFailure = StopOnFailure
            };
            lock (_pipelineLock)
            {
                config.StepNames = _steps.Select(s => s.Name).ToList();
            }
            return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// 从 JSON 字符串解析管线配置（返回配置对象，步骤实例需外部重新绑定）
        /// </summary>
        public static PipelineConfig? FromJson(string json)
            => JsonSerializer.Deserialize<PipelineConfig>(json);
    }
}
