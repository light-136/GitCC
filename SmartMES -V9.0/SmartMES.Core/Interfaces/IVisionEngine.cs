// ============================================================
// 文件：IVisionEngine.cs
// 用途：视觉系统接口定义 — 图像处理、管线、相机抽象
// 设计思路：
//   IImageProcessor 定义单步图像处理操作（灰度化、滤波、形态学等），
//   IVisionPipeline 将多个处理步骤串联成可配置的处理链，
//   ICameraService 抽象相机硬件（支持模拟和真实相机）。
//   所有接口使用 ImageData（纯字节数组），不依赖 WPF。
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Core.Interfaces
{
    /// <summary>
    /// 图像处理器接口 — 定义一个可复用的图像处理步骤。
    /// 每个实现类封装一种算法（如灰度化、高斯模糊、形态学腐蚀等）。
    /// </summary>
    public interface IImageProcessor
    {
        /// <summary>处理器名称（用于日志和管线配置）。</summary>
        string Name { get; }

        /// <summary>
        /// 对输入图像执行处理，返回处理后的图像。
        /// </summary>
        /// <param name="input">输入图像。</param>
        /// <param name="parameters">可选参数（如阈值、核大小等）。</param>
        /// <returns>处理后的图像。</returns>
        ImageData Process(ImageData input, Dictionary<string, object>? parameters = null);
    }

    /// <summary>
    /// 视觉处理管线接口 — 将多个图像处理步骤串联执行。
    /// 支持动态添加/移除步骤，以及带诊断信息的执行模式。
    /// </summary>
    public interface IVisionPipeline
    {
        /// <summary>
        /// 添加一个处理步骤到管线末尾。
        /// </summary>
        /// <param name="processor">图像处理器。</param>
        /// <param name="parameters">该步骤的参数。</param>
        void AddStep(IImageProcessor processor, Dictionary<string, object>? parameters = null);

        /// <summary>
        /// 移除指定索引的处理步骤。
        /// </summary>
        void RemoveStep(int index);

        /// <summary>
        /// 清空所有处理步骤。
        /// </summary>
        void Clear();

        /// <summary>
        /// 获取当前步骤数量。
        /// </summary>
        int StepCount { get; }

        /// <summary>
        /// 执行管线，返回最终处理结果。
        /// </summary>
        /// <param name="input">输入图像。</param>
        /// <returns>最终输出图像。</returns>
        ImageData Execute(ImageData input);

        /// <summary>
        /// 执行管线并返回每一步的中间结果（用于调试和诊断）。
        /// </summary>
        /// <param name="input">输入图像。</param>
        /// <returns>每一步的执行结果列表。</returns>
        List<PipelineStepResult> ExecuteWithDiagnostics(ImageData input);
    }

    /// <summary>
    /// 相机服务接口 — 抽象工业相机的采集和控制。
    /// 支持模拟相机和真实相机 SDK（如海康、Basler 等）。
    /// </summary>
    public interface ICameraService
    {
        /// <summary>相机是否已连接。</summary>
        bool IsConnected { get; }

        /// <summary>曝光时间（毫秒）。</summary>
        double ExposureMs { get; set; }

        /// <summary>增益。</summary>
        double Gain { get; set; }

        /// <summary>
        /// 连接相机。
        /// </summary>
        /// <param name="config">相机配置参数。</param>
        Task ConnectAsync(CameraConfig config);

        /// <summary>
        /// 断开相机连接。
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// 采集一帧图像。
        /// </summary>
        /// <returns>采集到的图像数据。</returns>
        Task<ImageData> CaptureAsync();

        /// <summary>图像采集完成时触发。</summary>
        event EventHandler<ImageData>? ImageCaptured;
    }
}
