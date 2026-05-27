// ============================================================
// 文件：IMotionCard.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义运动控制卡的硬件抽象接口（卡级别，贴近硬件 API）
// 设计思路：
//   IMotionCard 是最底层的硬件抽象，直接映射运动控制卡 SDK 的 API 函数。
//   不同品牌的运动卡（雷赛 LTDMC、固高 GTS、模拟卡 SimCard）
//   各自实现此接口，上层代码（IMotionController）只依赖此接口。
//   设计模式：依赖倒置（DIP）+ 适配器（Adapter）
//   注意：此接口的方法与硬件 API 命名相近，保留工程师熟悉的词汇。
//   线程安全：实现类必须保证线程安全（多轴并发调用）。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Models;

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 运动控制卡硬件抽象接口。
    /// 屏蔽不同品牌运动控制卡的底层 SDK 差异，提供统一的卡操作 API。
    /// 实现类须保证线程安全（多个 AxisController 可能并发调用）。
    /// </summary>
    public interface IMotionCard
    {
        // ----------------------------------------------------------------
        // 设备标识属性
        // ----------------------------------------------------------------

        /// <summary>控制卡唯一标识（如 "Card0"、"GTS-800"、"SimCard"）</summary>
        string CardId { get; }

        /// <summary>该卡支持的最大轴数</summary>
        int MaxAxisCount { get; }

        /// <summary>控制卡是否已初始化就绪</summary>
        bool IsInitialized { get; }

        // ----------------------------------------------------------------
        // 设备生命周期
        // ----------------------------------------------------------------

        /// <summary>
        /// 打开控制卡（加载驱动 DLL、初始化硬件、执行自检）。
        /// </summary>
        /// <returns>true 表示打开成功</returns>
        Task<bool> OpenCard();

        /// <summary>
        /// 关闭控制卡，释放驱动资源（应用退出时调用）。
        /// </summary>
        Task CloseCard();

        // ----------------------------------------------------------------
        // 轴参数设置
        // ----------------------------------------------------------------

        /// <summary>
        /// 向运动控制卡下载轴运动参数（每单位脉冲数、限位等）。
        /// </summary>
        /// <param name="axisIndex">轴索引（0-based）</param>
        /// <param name="parameters">运动参数对象</param>
        void SetAxisParam(int axisIndex, MotionParameters parameters);

        // ----------------------------------------------------------------
        // 轴使能
        // ----------------------------------------------------------------

        /// <summary>
        /// 使能指定轴（电机上电，建立力矩）。
        /// </summary>
        void EnableAxis(int axisIndex);

        /// <summary>
        /// 去使能指定轴（电机下电，自由转动）。
        /// </summary>
        void DisableAxis(int axisIndex);

        // ----------------------------------------------------------------
        // 运动控制指令
        // ----------------------------------------------------------------

        /// <summary>
        /// 启动单轴绝对/相对位置运动。
        /// </summary>
        /// <param name="axisIndex">轴索引</param>
        /// <param name="targetPosition">目标位置（mm）</param>
        /// <param name="velocity">运动速度（mm/s）</param>
        /// <param name="acceleration">加速度（mm/s²）</param>
        /// <param name="deceleration">减速度（mm/s²）</param>
        void StartMove(int axisIndex, double targetPosition, double velocity,
            double acceleration, double deceleration);

        /// <summary>
        /// 停止指定轴（减速停止，保持使能）。
        /// </summary>
        void StopMove(int axisIndex);

        /// <summary>
        /// 急停指定轴（立即切断脉冲，不减速）。
        /// </summary>
        void EmergencyStopAxis(int axisIndex);

        /// <summary>
        /// 启动回零序列。
        /// </summary>
        /// <param name="axisIndex">轴索引</param>
        /// <param name="config">回零配置参数</param>
        void StartHoming(int axisIndex, HomingConfig config);

        /// <summary>
        /// 启动点动（持续运动，需调用 StopMove 停止）。
        /// </summary>
        /// <param name="axisIndex">轴索引</param>
        /// <param name="velocity">点动速度（正值=正向，负值=反向，mm/s）</param>
        /// <param name="acceleration">加速度（mm/s²）</param>
        void StartJog(int axisIndex, double velocity, double acceleration);

        /// <summary>
        /// 清除轴错误（在排除故障后调用，使轴可重新运动）。
        /// </summary>
        void ClearAxisError(int axisIndex);

        /// <summary>
        /// 将当前位置设为零点（建立虚拟原点，不触发回零序列）。
        /// </summary>
        void SetZeroPosition(int axisIndex);

        // ----------------------------------------------------------------
        // 状态读取
        // ----------------------------------------------------------------

        /// <summary>
        /// 读取编码器反馈的实际位置（mm）。
        /// </summary>
        double ReadPosition(int axisIndex);

        /// <summary>
        /// 读取当前速度反馈（mm/s）。
        /// </summary>
        double ReadVelocity(int axisIndex);

        /// <summary>
        /// 读取轴的完整状态快照（包含限位、使能、运动中、错误码等）。
        /// </summary>
        AxisStatus ReadAxisStatus(int axisIndex);

        // ----------------------------------------------------------------
        // IO 操作
        // ----------------------------------------------------------------

        /// <summary>
        /// 写数字输出（true=ON，false=OFF）。
        /// </summary>
        void WriteIo(int ioIndex, bool value);

        /// <summary>
        /// 读数字输入当前状态。
        /// </summary>
        bool ReadIo(int ioIndex);

        /// <summary>
        /// 读模拟输入（0.0~10.0V）。
        /// </summary>
        double ReadAnalogInput(int ioIndex);

        /// <summary>
        /// 写模拟输出（0.0~10.0V）。
        /// </summary>
        void WriteAnalogOutput(int ioIndex, double value);

        // ----------------------------------------------------------------
        // 多轴插补
        // ----------------------------------------------------------------

        /// <summary>
        /// 启动多轴线性插补运动。
        /// </summary>
        /// <param name="axisIndices">参与插补的轴索引数组</param>
        /// <param name="targetPositions">各轴目标位置数组（mm，顺序与 axisIndices 对应）</param>
        /// <param name="velocity">合成路径速度（mm/s）</param>
        /// <param name="acceleration">加速度（mm/s²）</param>
        void StartInterpolation(int[] axisIndices, double[] targetPositions,
            double velocity, double acceleration);
    }
}

