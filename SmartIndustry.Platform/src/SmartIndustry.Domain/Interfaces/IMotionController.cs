// ============================================================
// 文件：IMotionController.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义运动控制器的高层业务接口（轴级别的指令抽象）
// 设计思路：
//   IMotionController 是运动控制领域的核心门面接口，与 IMotionCard 的区别：
//     - IMotionCard：硬件卡级别（脉冲、寄存器、IO），贴近硬件 API
//     - IMotionController：轴业务级别（物理单位 mm、速度 mm/s、软限位校验），贴近业务
//   此接口由 Application 层和 UI 层直接调用，屏蔽底层卡差异。
//   Hardware 层的 AxisController 实现此接口，内部委托给 IMotionCard。
//   设计要点：
//     1. 所有运动方法均为异步，支持 CancellationToken 取消
//     2. 运动完成通过 Task<bool> 返回，不阻塞 UI 线程
//     3. JogStart/JogStop 是例外——点动需要同步的立即响应
//     4. GetPosition/GetVelocity 为同步——实时显示需要最低延迟
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.ValueObjects;

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 运动控制器高层接口（轴业务级别）。
    /// 封装单个运动轴的完整生命周期操作，使用物理单位（mm、mm/s）。
    /// 所有实现类需保证线程安全，支持并发调用。
    /// </summary>
    public interface IMotionController : IDisposable
    {
        // ----------------------------------------------------------------
        // 控制器身份属性
        // ----------------------------------------------------------------

        /// <summary>轴名称（与 AxisConfig.Name 对应，如 "X轴"）</summary>
        string AxisName { get; }

        /// <summary>轴唯一标识符</summary>
        Guid AxisId { get; }

        /// <summary>当前轴状态</summary>
        AxisState CurrentState { get; }

        // ----------------------------------------------------------------
        // 生命周期
        // ----------------------------------------------------------------

        /// <summary>
        /// 异步初始化轴控制器（打开驱动、使能、加载参数）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>true 表示初始化成功</returns>
        Task<bool> InitializeAsync(CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 使能控制
        // ----------------------------------------------------------------

        /// <summary>
        /// 使能轴（电机上电，建立力矩）。
        /// </summary>
        Task EnableAxis(CancellationToken cancellationToken = default);

        /// <summary>
        /// 去使能轴（电机下电，轴自由转动）。
        /// </summary>
        Task DisableAxis(CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 运动指令（异步，运动完成时 Task 完成）
        // ----------------------------------------------------------------

        /// <summary>
        /// 绝对位置运动：移动到机械坐标系中的绝对位置。
        /// </summary>
        /// <param name="targetPosition">目标绝对位置（mm）</param>
        /// <param name="profile">运动参数（速度/加速度）</param>
        /// <param name="cancellationToken">取消令牌（取消时执行减速停止）</param>
        /// <returns>true 表示正常到位，false 表示被取消或出错</returns>
        Task<bool> MoveAbsoluteAsync(
            double targetPosition,
            MotionProfile profile,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 相对位置运动：从当前位置移动指定距离。
        /// </summary>
        /// <param name="distance">相对移动距离（mm，正值=正向，负值=反向）</param>
        /// <param name="profile">运动参数</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<bool> MoveRelativeAsync(
            double distance,
            MotionProfile profile,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 回零：执行原点搜寻序列，建立机械坐标系。
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>true 表示回零成功</returns>
        Task<bool> HomeAsync(CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 点动（同步，立即响应，无需等待完成）
        // ----------------------------------------------------------------

        /// <summary>
        /// 启动点动（持续运动，调用 JogStop 停止）。
        /// 点动使用同步接口确保按键响应的实时性。
        /// </summary>
        /// <param name="direction">运动方向（true=正向，false=反向）</param>
        /// <param name="velocity">点动速度（mm/s，应远小于 MaxVelocity）</param>
        void JogStart(bool direction, double velocity);

        /// <summary>
        /// 停止点动（减速至停止）。
        /// </summary>
        void JogStop();

        // ----------------------------------------------------------------
        // 停止指令
        // ----------------------------------------------------------------

        /// <summary>
        /// 减速停止（按配置减速度平滑停止，不丢步）。
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 急停（立即停止脉冲输出，可能引起机械冲击）。
        /// 只在紧急情况或安全保护触发时调用。
        /// </summary>
        void EmergencyStop();

        // ----------------------------------------------------------------
        // 实时状态读取（同步，高频调用）
        // ----------------------------------------------------------------

        /// <summary>
        /// 获取当前实际位置（编码器反馈，mm）。
        /// </summary>
        double GetPosition();

        /// <summary>
        /// 获取当前实际速度（mm/s，正值=正向，负值=反向）。
        /// </summary>
        double GetVelocity();

        /// <summary>
        /// 获取轴的完整状态枚举值。
        /// </summary>
        AxisState GetAxisState();

        // ----------------------------------------------------------------
        // 参数设置
        // ----------------------------------------------------------------

        /// <summary>
        /// 动态更新轴的运动参数（不重新初始化，对后续指令生效）。
        /// </summary>
        /// <param name="axisConfig">新的轴配置实体</param>
        Task SetParameters(Entities.AxisConfig axisConfig, CancellationToken cancellationToken = default);
    }
}
