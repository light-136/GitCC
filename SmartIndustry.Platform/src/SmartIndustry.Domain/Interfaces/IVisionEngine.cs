// ============================================================
// 文件：IVisionEngine.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义视觉引擎的高层抽象接口，屏蔽视觉库实现细节
// 设计思路：
//   视觉引擎接口是所有视觉算法的统一入口，抽象了：
//     - 图像采集（CaptureAsync）
//     - 任务执行（ExecuteTaskAsync，按任务配置分发算法）
//     - 模型管理（LoadModelAsync）
//     - ROI 设置（SetROI）
//     - 相机管理（GetCameraList）
//     - 标定（CalibrateAsync）
//   不同视觉引擎实现（Halcon、OpenCV、深度学习推理引擎）
//   均实现此接口，Application 层通过 DI 注入所需实现。
//   设计模式：策略（Strategy）+ 门面（Facade）
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Models;
using SmartIndustry.Domain.ValueObjects;

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 视觉引擎高层抽象接口。
    /// 封装从图像采集到结果输出的完整视觉处理管道。
    /// </summary>
    public interface IVisionEngine : IAsyncDisposable
    {
        // ----------------------------------------------------------------
        // 引擎状态属性
        // ----------------------------------------------------------------

        /// <summary>引擎唯一标识（如 "HalconEngine-Cam0"）</summary>
        string EngineId { get; }

        /// <summary>引擎是否已就绪（初始化完成且相机已连接）</summary>
        bool IsReady { get; }

        // ----------------------------------------------------------------
        // 生命周期
        // ----------------------------------------------------------------

        /// <summary>
        /// 异步初始化视觉引擎（连接相机、加载算法库、预热模型）。
        /// </summary>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 图像采集
        // ----------------------------------------------------------------

        /// <summary>
        /// 触发采集一帧图像。
        /// </summary>
        /// <param name="cameraId">要采集的相机 ID</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>采集到的原始图像数据</returns>
        Task<ImageData> CaptureAsync(string cameraId, CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 视觉任务执行
        // ----------------------------------------------------------------

        /// <summary>
        /// 执行视觉任务（统一入口，内部按 TaskType 分发到对应算法分支）。
        /// </summary>
        /// <param name="task">视觉任务配置实体（包含算法参数和判定阈值）</param>
        /// <param name="image">输入图像（null 时引擎自动从绑定相机采集）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>包含得分、判定结果和结果图像路径的通用视觉结果</returns>
        Task<VisionResult<object>> ExecuteTaskAsync(
            VisionTask task,
            ImageData? image = null,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 模型管理
        // ----------------------------------------------------------------

        /// <summary>
        /// 加载视觉算法模型（模板、训练权重等）到内存。
        /// </summary>
        /// <param name="modelId">模型在引擎内的唯一标识</param>
        /// <param name="modelPath">模型文件路径</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task LoadModelAsync(string modelId, string modelPath, CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // ROI 设置
        // ----------------------------------------------------------------

        /// <summary>
        /// 为指定视觉任务设置感兴趣区域（ROI）。
        /// </summary>
        /// <param name="taskId">视觉任务 ID</param>
        /// <param name="region">ROI 区域定义</param>
        void SetROI(Guid taskId, VisionRegion region);

        // ----------------------------------------------------------------
        // 相机管理
        // ----------------------------------------------------------------

        /// <summary>
        /// 获取系统中所有可用相机的标识列表。
        /// </summary>
        /// <returns>相机 ID 字符串列表</returns>
        Task<IReadOnlyList<string>> GetCameraList(CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 标定
        // ----------------------------------------------------------------

        /// <summary>
        /// 执行相机标定（内参标定、手眼标定或多相机外参标定）。
        /// </summary>
        /// <param name="cameraId">要标定的相机 ID</param>
        /// <param name="calibrationPoints">标定点坐标对（图像像素坐标 -> 机械坐标）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>标定结果数据（仿射矩阵、像素精度、RMS 误差）</returns>
        Task<CalibrationData> CalibrateAsync(
            string cameraId,
            IEnumerable<(double PixelX, double PixelY, double MechX, double MechY)> calibrationPoints,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 资源释放（继承自 IAsyncDisposable）
        // ----------------------------------------------------------------
        // 子类实现 DisposeAsync() 方法，释放相机句柄、算法内存、线程池等资源
    }
}

