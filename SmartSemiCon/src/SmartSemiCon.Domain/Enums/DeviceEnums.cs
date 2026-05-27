// ============================================================
// 文件：DeviceState.cs
// 用途：设备状态枚举定义
// 设计思路：
//   定义半导体设备的完整生命周期状态，符合SEMI标准。
//   状态转换由 DeviceStateMachine 管理，此处仅定义枚举值。
//   每个状态对应设备的一种运行模式，UI/报警/流程均依赖此状态。
// ============================================================

namespace SmartSemiCon.Domain.Enums
{
    /// <summary>
    /// 设备运行状态 — 定义设备从初始化到运行的完整生命周期。
    /// 状态流转：Idle → Init → Auto/Manual → Alarm/EmergencyStop → Idle
    /// </summary>
    public enum DeviceState
    {
        /// <summary>空闲状态 — 设备已上电但未执行任何操作</summary>
        Idle = 0,

        /// <summary>初始化状态 — 设备正在执行回原点、自检等初始化流程</summary>
        Init = 1,

        /// <summary>自动运行状态 — 设备按照Recipe自动执行生产流程</summary>
        Auto = 2,

        /// <summary>手动操作状态 — 操作员手动控制设备（JOG、点位运动等）</summary>
        Manual = 3,

        /// <summary>报警状态 — 设备检测到异常，需要处理后才能恢复</summary>
        Alarm = 4,

        /// <summary>急停状态 — 紧急停止，所有运动立即停止，需要手动复位</summary>
        EmergencyStop = 5,

        /// <summary>暂停状态 — 自动运行中暂停，可恢复继续运行</summary>
        Paused = 6,

        /// <summary>维护状态 — 设备进入维护模式，仅允许维护操作</summary>
        Maintenance = 7
    }

    /// <summary>
    /// 轴运动状态 — 描述单个运动轴的实时状态。
    /// 运动控制模块根据此状态决定是否允许新的运动指令。
    /// </summary>
    public enum AxisState
    {
        /// <summary>未初始化 — 轴驱动器未使能</summary>
        NotReady = 0,

        /// <summary>就绪 — 驱动器已使能，可接受运动指令</summary>
        Ready = 1,

        /// <summary>运动中 — 轴正在执行运动指令</summary>
        Moving = 2,

        /// <summary>已完成 — 运动指令执行完毕，到达目标位置</summary>
        Done = 3,

        /// <summary>错误 — 轴发生故障（跟随误差过大、驱动器报警等）</summary>
        Error = 4,

        /// <summary>回原点中 — 轴正在执行Homing流程</summary>
        Homing = 5,

        /// <summary>JOG运动中 — 轴正在执行点动运动</summary>
        Jogging = 6,

        /// <summary>已禁用 — 轴被软件禁用，不响应运动指令</summary>
        Disabled = 7
    }

    /// <summary>
    /// 运动模式 — 定义运动指令的类型。
    /// 运动控制器根据此枚举选择对应的运动算法。
    /// </summary>
    public enum MotionMode
    {
        /// <summary>绝对定位运动 — 移动到指定的绝对坐标</summary>
        Absolute = 0,

        /// <summary>相对定位运动 — 从当前位置移动指定距离</summary>
        Relative = 1,

        /// <summary>JOG点动 — 按下运动、松开停止</summary>
        Jog = 2,

        /// <summary>回原点 — 执行回原点流程</summary>
        Home = 3,

        /// <summary>直线插补 — 多轴协调直线运动</summary>
        LinearInterpolation = 4,

        /// <summary>圆弧插补 — 多轴协调圆弧运动</summary>
        CircularInterpolation = 5,

        /// <summary>电子齿轮 — 从轴跟随主轴按比例运动</summary>
        ElectronicGear = 6,

        /// <summary>CAM电子凸轮 — 按凸轮曲线表运动</summary>
        Cam = 7
    }

    /// <summary>
    /// 报警级别 — 按严重程度分级，决定系统响应方式。
    /// </summary>
    public enum AlarmLevel
    {
        /// <summary>提示 — 仅记录日志，不影响运行</summary>
        Info = 0,

        /// <summary>警告 — 提醒操作员注意，不停机</summary>
        Warning = 1,

        /// <summary>轻故障 — 暂停当前流程，可自动恢复</summary>
        Light = 2,

        /// <summary>重故障 — 停止生产，需要手动处理</summary>
        Heavy = 3,

        /// <summary>致命 — 立即急停，需要设备维修</summary>
        Fatal = 4
    }

    /// <summary>
    /// 通讯连接状态 — TCP/SECS等通讯通道的状态。
    /// </summary>
    public enum ConnectionState
    {
        /// <summary>已断开</summary>
        Disconnected = 0,

        /// <summary>连接中</summary>
        Connecting = 1,

        /// <summary>已连接</summary>
        Connected = 2,

        /// <summary>重连中 — 连接断开后自动尝试重新连接</summary>
        Reconnecting = 3,

        /// <summary>错误 — 连接异常，需要人工介入</summary>
        Error = 4
    }

    /// <summary>
    /// GEM控制状态 — SEMI E30标准定义的设备控制状态。
    /// Host通过Remote Command切换设备的控制模式。
    /// </summary>
    public enum GemControlState
    {
        /// <summary>离线-设备未连接 — Equipment未与Host建立通讯</summary>
        OfflineEquipmentOffline = 0,

        /// <summary>离线-尝试上线 — Equipment正在尝试与Host建立通讯</summary>
        OfflineAttemptOnline = 1,

        /// <summary>离线-Host离线 — Host端未响应</summary>
        OfflineHostOffline = 2,

        /// <summary>在线-本地控制 — 设备在线但由本地操作员控制</summary>
        OnlineLocal = 3,

        /// <summary>在线-远程控制 — 设备在线且由Host远程控制</summary>
        OnlineRemote = 4
    }

    /// <summary>
    /// GEM通讯状态 — HSMS连接层的通讯状态。
    /// </summary>
    public enum GemCommunicationState
    {
        /// <summary>禁用 — 通讯功能未启用</summary>
        Disabled = 0,

        /// <summary>等待通讯 — 已启用，等待对方连接</summary>
        WaitCommunicating = 1,

        /// <summary>通讯中 — 通讯已建立，可收发消息</summary>
        Communicating = 2
    }

    /// <summary>
    /// HSMS连接状态 — HSMS-SS协议的传输层状态。
    /// </summary>
    public enum HsmsConnectionState
    {
        /// <summary>未连接</summary>
        NotConnected = 0,

        /// <summary>已连接但未Select</summary>
        Connected = 1,

        /// <summary>已Select — 可以收发SECS消息</summary>
        Selected = 2
    }

    /// <summary>
    /// 视觉引擎类型 — 支持的视觉库类型。
    /// 通过抽象接口 IVisionEngine 统一不同视觉库的调用方式。
    /// </summary>
    public enum VisionEngineType
    {
        /// <summary>模拟引擎 — 无需硬件，用于开发测试</summary>
        Simulation = 0,

        /// <summary>Halcon — MVTec Halcon视觉库</summary>
        Halcon = 1,

        /// <summary>OpenCV — 开源计算机视觉库</summary>
        OpenCV = 2,

        /// <summary>VisionPro — 康耐视VisionPro视觉库</summary>
        VisionPro = 3
    }

    /// <summary>
    /// 触发模式 — 相机图像采集的触发方式。
    /// </summary>
    public enum TriggerMode
    {
        /// <summary>软触发 — 由软件发送采集命令</summary>
        Software = 0,

        /// <summary>硬触发 — 由外部IO信号触发采集</summary>
        Hardware = 1,

        /// <summary>连续采集 — 相机持续采集图像</summary>
        Continuous = 2,

        /// <summary>编码器触发 — 由编码器脉冲触发（飞拍模式）</summary>
        Encoder = 3
    }

    /// <summary>
    /// 用户角色 — 操作权限等级。
    /// </summary>
    public enum UserRole
    {
        /// <summary>操作员 — 仅能执行基本操作（启动/停止/查看）</summary>
        Operator = 0,

        /// <summary>工程师 — 可修改参数、配方、手动控制</summary>
        Engineer = 1,

        /// <summary>管理员 — 完全权限，包括用户管理和系统配置</summary>
        Administrator = 2
    }

    /// <summary>
    /// 日志级别 — 日志分类标准。
    /// </summary>
    public enum LogLevel
    {
        /// <summary>调试 — 开发调试信息</summary>
        Debug = 0,

        /// <summary>信息 — 正常运行信息</summary>
        Info = 1,

        /// <summary>警告 — 需要注意的情况</summary>
        Warning = 2,

        /// <summary>错误 — 错误但不影响系统运行</summary>
        Error = 3,

        /// <summary>致命 — 系统无法继续运行</summary>
        Fatal = 4
    }

    /// <summary>
    /// 运动控制卡类型 — 支持的硬件驱动类型。
    /// </summary>
    public enum MotionCardType
    {
        /// <summary>模拟卡 — 无需硬件，用于开发测试</summary>
        Simulation = 0,

        /// <summary>EtherCAT — 工业以太网运动控制</summary>
        EtherCAT = 1,

        /// <summary>PLC — 通过PLC控制运动轴</summary>
        PLC = 2,

        /// <summary>板卡 — 插在PC上的运动控制板卡</summary>
        MotionBoard = 3
    }
}
