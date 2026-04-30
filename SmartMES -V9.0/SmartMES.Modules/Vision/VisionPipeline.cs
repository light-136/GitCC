// ============================================================
// 文件：VisionPipeline.cs
// 用途：图像处理管线 — 可配置的链式处理步骤
// 设计思路：
//   工业视觉通常需要多个处理步骤串联执行，如：
//   灰度化 → 高斯模糊 → OTSU阈值 → 形态学 → Blob分析
//   管线模式将这些步骤组织为可配置的处理链：
//   - 支持动态添加/移除/重排步骤
//   - 支持保存中间结果用于诊断
//   - 每个步骤都实现 IImageProcessor 接口
// ============================================================

using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;

namespace SmartMES.Modules.Vision
{
    /// <summary>
    /// 图像处理管线 — 链式执行多个处理步骤。
    ///
    /// 使用示例：
    ///   var pipeline = new VisionPipeline();
    ///   pipeline.AddStep(new GrayscaleProcessor());
    ///   pipeline.AddStep(new GaussianBlurProcessor());
    ///   pipeline.AddStep(new OtsuThresholdProcessor());
    ///   ImageData result = pipeline.Execute(inputImage);
    /// </summary>
    public class VisionPipeline : IVisionPipeline
    {
        // 处理步骤列表（有序），每个步骤包含处理器及其参数
        private readonly List<(IImageProcessor Processor, Dictionary<string, object>? Parameters)> _steps = new();
        private readonly object _lock = new();

        /// <summary>步骤数量。</summary>
        public int StepCount
        {
            get { lock (_lock) { return _steps.Count; } }
        }

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        /// <summary>
        /// 添加处理步骤到管线末尾。
        /// </summary>
        /// <param name="processor">图像处理器。</param>
        /// <param name="parameters">该步骤的参数。</param>
        public void AddStep(IImageProcessor processor, Dictionary<string, object>? parameters = null)
        {
            lock (_lock)
            {
                _steps.Add((processor, parameters));
            }
        }

        /// <summary>
        /// 在指定位置插入处理步骤。
        /// </summary>
        public void InsertStep(int index, IImageProcessor processor, Dictionary<string, object>? parameters = null)
        {
            lock (_lock)
            {
                _steps.Insert(index, (processor, parameters));
            }
        }

        /// <summary>
        /// 移除指定索引的处理步骤。
        /// </summary>
        public void RemoveStep(int index)
        {
            lock (_lock)
            {
                if (index >= 0 && index < _steps.Count)
                    _steps.RemoveAt(index);
            }
        }

        /// <summary>
        /// 清除所有步骤。
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _steps.Clear();
            }
        }

        /// <summary>
        /// 执行管线 — 按顺序执行所有步骤。
        /// 前一步的输出作为后一步的输入。
        /// </summary>
        /// <param name="input">输入图像。</param>
        /// <returns>最终处理结果。</returns>
        public ImageData Execute(ImageData input)
        {
            lock (_lock)
            {
                var current = input;

                foreach (var (processor, parameters) in _steps)
                {
                    current = processor.Process(current, parameters);
                }

                return current;
            }
        }

        /// <summary>
        /// 带诊断信息的管线执行 — 保存每个步骤的中间结果。
        /// 用于调试和参数调优。
        /// </summary>
        /// <param name="input">输入图像。</param>
        /// <returns>步骤结果列表（包含步骤名称、耗时、中间图像）。</returns>
        public List<PipelineStepResult> ExecuteWithDiagnostics(ImageData input)
        {
            lock (_lock)
            {
                var results = new List<PipelineStepResult>();
                var current = input;

                foreach (var (processor, parameters) in _steps)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var output = processor.Process(current, parameters);
                    sw.Stop();

                    results.Add(new PipelineStepResult
                    {
                        StepName = processor.Name,
                        Duration = sw.Elapsed,
                        Output = output
                    });

                    Log($"[管线] {processor.Name} 完成，耗时 {sw.Elapsed.TotalMilliseconds:F1}ms，" +
                        $"输出 {output.Width}×{output.Height}×{output.Channels}");

                    current = output;
                }

                return results;
            }
        }

        /// <summary>
        /// 获取所有步骤名称（按执行顺序）。
        /// </summary>
        public List<string> GetStepNames()
        {
            lock (_lock)
            {
                return _steps.Select(s => s.Processor.Name).ToList();
            }
        }

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);
    }
}
