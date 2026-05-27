// ============================================================
// 文件：SystemEnums.cs
// 层次：领域层 (Domain Layer)
// 职责：定义整个工业智能平台所有业务枚举，集中管理状态语义
// 设计思路：
//   枚举作为领域语言的核心词汇表，每个枚举值均以中文注释
//   说明其业务含义，保证跨团队沟通一致性。
//   所有枚举从 0 开始显式赋值，防止序列化/反序列化
//   因默认值变动导致数据错乱。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Domain.Enums
{
    // ----------------------------------------------------------------
    // 设备状态枚举
    // 描述运动控制卡、PLC、机械单元等设备的生命周期状态机节点。
    // 状态转换路径：Idle -> Initializing -> Ready -> Running
    //              Running -> Paused / Running -> Stopping -> Idle
    //              任何状态 -> Faulted / EmergencyStop
    // ----------------------------------------------------------------
    public enum DeviceState
    {
        /// <summary>空闲：设备已上电但未初始化或初始化完成待命</summary>
        Idle = 0,

        /// <summary>初始化中：正在执行自检、复位、参数加载等启动序列</summary>
        Initializing = 1,

        /// <summary>就绪：初始化完成，等待任务下发</summary>
        Ready = 2,

        /// <summary>运行中：正在执行运动指令或生产任务</summary>
        Running = 3,

        /// <summary>暂停：任务被挂起，可恢复执行</summary>
        Paused = 4,

        /// <summary>停止中：正在执行减速停止序列，尚未完全静止</summary>
        Stopping = 5,

        /// <summary>故障：检测到非预期错误，需要人工干预或自动恢复</summary>
        Faulted = 6,

        /// <summary>维护模式：进入工程调试、参数整定、硬件维护状态</summary>
        Maintenance = 7,

        /// <summary>急停：安全回路断开或 EStop 触发，立即停止所有运动</summary>
        EmergencyStop = 8
    }

    // ----------------------------------------------------------------
    // 轴状态枚举
    // 描述单个运动轴的实时状态，与运动控制卡寄存器状态一一对应。
    // ----------------------------------------------------------------
    public enum AxisState
    {
        /// <summary>未使能：伺服/步进电机处于自由状态，不响应运动指令</summary>
        Disabled = 0,

        /// <summary>已使能：电机上电保持力矩，等待运动指令</summary>
        Enabled = 1,

        /// <summary>回零中：正在执行原点搜寻序列</summary>
        Homing = 2,

        /// <summary>运动中：轴正在按照规划轨迹移动</summary>
        Moving = 3,

        /// <summary>错误：轴发生位置偏差过大、过载、编码器故障等错误</summary>
        Error = 4,

        /// <summary>限位触发：轴碰到硬限位或软限位，运动被强制停止</summary>
        Limit = 5
    }

    // ----------------------------------------------------------------
    // 报警等级枚举
    // 参考 IEC 62682 报警管理标准设计，从低到高表示严重程度。
    // ----------------------------------------------------------------
    public enum AlarmLevel
    {
        /// <summary>信息：仅作记录，不影响生产，无需操作员响应</summary>
        Info = 0,

        /// <summary>警告：存在潜在风险，建议操作员关注但可继续生产</summary>
        Warning = 1,

        /// <summary>错误：生产受到影响，需要操作员介入处理</summary>
        Error = 2,

        /// <summary>严重：生产中断，需要工程师级别人员立即响应</summary>
        Critical = 3,

        /// <summary>致命：可能危及人员或设备安全，系统自动进入安全模式</summary>
        Fatal = 4
    }

    // ----------------------------------------------------------------
    // 报警类别枚举
    // 按照功能模块对报警进行分类，便于过滤、路由和统计分析。
    // ----------------------------------------------------------------
    public enum AlarmCategory
    {
        /// <summary>运动类：轴位置偏差、限位、速度异常等运动控制相关报警</summary>
        Motion = 0,

        /// <summary>视觉类：相机掉线、图像质量差、算法超时等视觉模块报警</summary>
        Vision = 1,

        /// <summary>通信类：网络断线、协议超时、数据校验失败等通信报警</summary>
        Communication = 2,

        /// <summary>安全类：急停触发、光栅遮断、安全门开启等安全相关报警</summary>
        Safety = 3,

        /// <summary>系统类：内存不足、磁盘空间低、软件异常等平台系统报警</summary>
        System = 4,

        /// <summary>工艺类：配方参数越限、品质超差、良率低下等工艺过程报警</summary>
        Process = 5
    }

    // ----------------------------------------------------------------
    // 用户角色枚举
    // 实现 RBAC（基于角色的访问控制），权限从低到高递增。
    // ----------------------------------------------------------------
    public enum UserRole
    {
        /// <summary>查看者：只读权限，可监控设备状态和查看报表</summary>
        Viewer = 0,

        /// <summary>操作员：可启动/停止设备、确认报警、执行标准配方</summary>
        Operator = 1,

        /// <summary>工程师：可修改配方参数、调整运动参数、查看调试日志</summary>
        Engineer = 2,

        /// <summary>管理员：拥有所有权限，包括用户管理、系统配置</summary>
        Administrator = 3
    }

    // ----------------------------------------------------------------
    // 通信协议枚举
    // 支持工业现场常用通信协议，Infrastructure 层据此实例化对应驱动。
    // ----------------------------------------------------------------
    public enum CommunicationProtocol
    {
        /// <summary>TCP 客户端模式：主动连接远端服务器</summary>
        TcpClient = 0,

        /// <summary>TCP 服务端模式：监听端口等待客户端连接</summary>
        TcpServer = 1,

        /// <summary>串口通信：RS-232/RS-485/RS-422 等串行接口</summary>
        Serial = 2,

        /// <summary>Modbus 协议：支持 RTU/TCP 变体，连接 PLC 和仪表</summary>
        Modbus = 3,

        /// <summary>OPC UA：工业4.0标准协议，安全、可靠、跨平台</summary>
        OpcUa = 4,

        /// <summary>MQTT：轻量级发布订阅协议，适合 IoT 场景</summary>
        Mqtt = 5
    }

    // ----------------------------------------------------------------
    // 连接状态枚举
    // 描述通信通道的连接生命周期，驱动 UI 图标颜色和自动重连逻辑。
    // ----------------------------------------------------------------
    public enum ConnectionState
    {
        /// <summary>未连接：通道未建立或已主动断开</summary>
        Disconnected = 0,

        /// <summary>连接中：正在执行握手或三次握手序列</summary>
        Connecting = 1,

        /// <summary>已连接：通道正常，可收发数据</summary>
        Connected = 2,

        /// <summary>错误：连接异常中断，等待自动重连或人工干预</summary>
        Error = 3
    }

    // ----------------------------------------------------------------
    // 视觉任务类型枚举
    // 对应视觉引擎支持的算法类别，Application 层据此分发任务。
    // ----------------------------------------------------------------
    public enum VisionTaskType
    {
        /// <summary>模板匹配：在图像中定位预定义模板的位置和角度</summary>
        PatternMatch = 0,

        /// <summary>斑点分析：检测、计数、测量图像中的连通区域</summary>
        BlobAnalysis = 1,

        /// <summary>尺寸测量：边缘检测和几何尺寸计算</summary>
        Measurement = 2,

        /// <summary>OCR 识别：读取图像中的字符、条码、二维码</summary>
        OCR = 3,

        /// <summary>缺陷检测：基于深度学习的表面缺陷识别和分类</summary>
        DefectDetection = 4,

        /// <summary>标定：相机内参标定、手眼标定、多相机外参标定</summary>
        Calibration = 5
    }

    // ----------------------------------------------------------------
    // 运动模式枚举
    // 描述运动指令的运行模式，决定运动控制器内部轨迹规划策略。
    // ----------------------------------------------------------------
    public enum MotionMode
    {
        /// <summary>点动：持续按压时以低速运动，松开立即停止</summary>
        Jog = 0,

        /// <summary>绝对定位：移动到机械坐标系中的绝对位置</summary>
        Absolute = 1,

        /// <summary>相对定位：相对当前位置移动指定增量</summary>
        Relative = 2,

        /// <summary>回零：执行原点搜寻序列，建立机械坐标系</summary>
        Home = 3,

        /// <summary>插补：多轴协同运动，按指定轨迹类型联动</summary>
        Interpolation = 4
    }

    // ----------------------------------------------------------------
    // 插补类型枚举
    // 多轴插补运动的轨迹形状，决定路径规划算法的选择。
    // ----------------------------------------------------------------
    public enum InterpolationType
    {
        /// <summary>直线插补：两点之间的最短路径运动</summary>
        Linear = 0,

        /// <summary>圆弧插补：在平面内沿圆弧轨迹运动</summary>
        Circular = 1,

        /// <summary>螺旋插补：圆弧运动同时叠加轴向直线运动</summary>
        Helical = 2
    }

    // ----------------------------------------------------------------
    // 日志等级枚举
    // 与 Microsoft.Extensions.Logging.LogLevel 语义对齐，
    // 保证平台日志系统可无缝对接 Serilog、NLog 等主流框架。
    // ----------------------------------------------------------------
    public enum LogLevel
    {
        /// <summary>调试：开发阶段的细粒度追踪信息，生产环境默认关闭</summary>
        Debug = 0,

        /// <summary>信息：正常业务流程的关键节点记录</summary>
        Info = 1,

        /// <summary>警告：非预期但不影响主流程的异常情况</summary>
        Warning = 2,

        /// <summary>错误：影响功能的异常，需要记录完整堆栈</summary>
        Error = 3,

        /// <summary>致命：导致应用崩溃或数据损坏的极端错误</summary>
        Fatal = 4
    }

    // ----------------------------------------------------------------
    // 配方状态枚举
    // 实现配方版本管理的生命周期控制，防止草稿配方被误用于生产。
    // ----------------------------------------------------------------
    public enum RecipeState
    {
        /// <summary>草稿：正在编辑中，未经过验证，禁止生产执行</summary>
        Draft = 0,

        /// <summary>激活：经过验证并激活，当前正在使用的生产配方</summary>
        Active = 1,

        /// <summary>归档：已被新版本替代，仅作历史追溯，禁止重新激活</summary>
        Archived = 2
    }

    // ----------------------------------------------------------------
    // 文件操作类型枚举
    // 领域事件和审计日志中记录文件操作的操作类型。
    // ----------------------------------------------------------------
    public enum FileOperationType
    {
        /// <summary>读取：从文件系统读取文件内容</summary>
        Read = 0,

        /// <summary>写入：向文件写入或覆盖内容</summary>
        Write = 1,

        /// <summary>复制：将文件复制到新路径</summary>
        Copy = 2,

        /// <summary>移动：将文件移动到新路径（原路径不再存在）</summary>
        Move = 3,

        /// <summary>删除：将文件移入回收站或永久删除</summary>
        Delete = 4,

        /// <summary>压缩：将文件或目录打包压缩</summary>
        Compress = 5,

        /// <summary>解压：从压缩包中提取文件</summary>
        Decompress = 6
    }

    // ----------------------------------------------------------------
    // 数据库提供者枚举
    // 平台支持多数据库部署，Infrastructure 层据此创建对应连接字符串
    // 和 ORM 驱动，实现数据库无关的上层代码设计。
    // ----------------------------------------------------------------
    public enum DatabaseProvider
    {
        /// <summary>SQLite：嵌入式数据库，适合单机小数据量场景</summary>
        SQLite = 0,

        /// <summary>MySQL：开源关系型数据库，适合中等规模生产数据</summary>
        MySQL = 1,

        /// <summary>SQL Server：微软企业级数据库，适合大规模 MES 集成</summary>
        SqlServer = 2,

        /// <summary>PostgreSQL：开源高性能数据库，适合高并发时序数据存储</summary>
        PostgreSQL = 3
    }

    // ----------------------------------------------------------------
    // 协同会话角色枚举
    // 多人实时协同功能中，控制用户对共享会话状态的操作权限。
    // ----------------------------------------------------------------
    public enum CollaborationRole
    {
        /// <summary>所有者：会话创建者，可管理其他成员权限并关闭会话</summary>
        Owner = 0,

        /// <summary>编辑者：可读写共享状态，参与协同操作</summary>
        Editor = 1,

        /// <summary>查看者：只读共享状态，不可修改协同数据</summary>
        Viewer = 2
    }
}
