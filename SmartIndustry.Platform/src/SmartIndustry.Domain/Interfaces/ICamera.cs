// ============================================================
// 文件：ICamera.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义相机设备的硬件抽象接口
// 设计思路：
//   ICamera 是对工业相机（海康、大恒、Basler、USB 相机）的统一抽象。
//   区别于 IVisionEngine：
//     - ICamera：只负责图像采集（打开/关闭/抓图/参数设置）
//     - IVisionEngine：负责完整视觉处理管道（采集 + 算法 + 结果）
//   ICamera 被 IVisionEngine 的实现类内部依赖，也可被需要
//   直接控制相机（如实时预览、手动对焦）的场景独立使用。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Models;

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 工业相机硬件抽象接口。
    /// 封装相机的打开/关闭/采集/参数设置等基础操作。
    /// </summary>
    public interface ICamera : IAsyncDisposable
    {
        // ----------------------------------------------------------------
        // 相机标识属性
        // ----------------------------------------------------------------

        /// <summary>相机唯一标识符（序列号或用户自定义 ID）</summary>
        string CameraId { get; }

        /// <summary>相机型号名称（如 "MV-CA013-20UC"）</summary>
        string ModelName { get; }

        /// <summary>相机当前连接状态</summary>
        bool IsConnected { get; }

        // ----------------------------------------------------------------
        // 生命周期
        // ----------------------------------------------------------------

        /// <summary>
        /// 异步打开相机连接（枚举设备、建立通信、配置初始参数）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>true 表示打开成功</returns>
        Task<bool> OpenAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步关闭相机连接（停止采集、释放相机句柄）。
        /// </summary>
        Task CloseAsync(CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 图像采集
        // ----------------------------------------------------------------

        /// <summary>
        /// 软件触发采集一帧图像（适用于软触发模式）。
        /// </summary>
        /// <param name="timeoutMs">采集超时时间（毫秒，默认 3000ms）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>采集到的图像数据</returns>
        Task<ImageData> GrabAsync(int timeoutMs = 3000, CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 相机参数设置
        // ----------------------------------------------------------------

        /// <summary>
        /// 设置曝光时间。
        /// </summary>
        /// <param name="exposureUs">曝光时间（微秒，μs）</param>
        void SetExposure(double exposureUs);

        /// <summary>
        /// 设置增益（模拟增益）。
        /// </summary>
        /// <param name="gain">增益值（dB，具体范围由相机型号决定）</param>
        void SetGain(double gain);

        /// <summary>
        /// 设置触发模式。
        /// </summary>
        /// <param name="isSoftwareTrigger">true=软件触发，false=外部硬触发</param>
        void SetTriggerMode(bool isSoftwareTrigger);

        // ----------------------------------------------------------------
        // 相机信息查询
        // ----------------------------------------------------------------

        /// <summary>
        /// 获取相机传感器分辨率（图像尺寸）。
        /// </summary>
        /// <returns>(Width, Height) 元组，单位像素</returns>
        (int Width, int Height) GetResolution();

        /// <summary>
        /// 获取当前曝光时间（微秒）。
        /// </summary>
        double GetExposure();

        /// <summary>
        /// 获取当前增益值（dB）。
        /// </summary>
        double GetGain();
    }
}
