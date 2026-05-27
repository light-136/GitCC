// ============================================================
// 文件：CoreInterfaces.cs
// 用途：核心接口定义 — 系统所有模块的契约
// 设计思路：
//   面向接口编程是工业软件的基本原则。
//   所有模块通过接口通信，实现类由DI容器注入。
//   接口定义在Domain层，实现在Infrastructure/Hardware/Application层。
//   这样上层代码永远不依赖具体实现，可自由替换。
//
//   依赖方向：UI → Application → Hardware → Infrastructure → Domain
//   所有层都可以引用Domain层的接口。
// ============================================================

using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Events;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.Domain.Interfaces
{
    // =========================================
    // 事件总线接口 — 模块间解耦通信的核心
    // =========================================

    /// <summary>
    /// 事件总线接口 — 发布/订阅模式的消息中介。
    /// 所有模块通过事件总线进行松耦合通信。
    /// 发布者只管发事件，不关心谁在监听；订阅者只管监听，不关心谁发了事件。
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        /// 发布事件 — 通知所有订阅者。
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <param name="domainEvent">事件数据</param>
        void Publish<TEvent>(TEvent domainEvent) where TEvent : DomainEvent;

        /// <summary>
        /// 订阅事件 — 注册事件处理器。
        /// </summary>
        /// <typeparam name="TEvent">要订阅的事件类型</typeparam>
        /// <param name="handler">事件处理委托</param>
        /// <returns>订阅令牌，调用Dispose取消订阅</returns>
        IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEvent;

        /// <summary>
        /// 异步订阅事件。
        /// </summary>
        IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : DomainEvent;
    }

    // =========================================
    // 通讯接口
    // =========================================

    /// <summary>
    /// TCP通讯服务接口 — 定义通讯通道的统一契约。
    /// 无论是连接PLC、运动控制器还是视觉系统，都使用相同接口。
    /// </summary>
    public interface ICommunicationService : IDisposable
    {
        /// <summary>通道名称标识</summary>
        string ChannelName { get; }

        /// <summary>当前连接状态</summary>
        ConnectionState State { get; }

        /// <summary>是否已连接</summary>
        bool IsConnected { get; }

        /// <summary>连接到远程端</summary>
        Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

        /// <summary>断开连接</summary>
        Task DisconnectAsync();

        /// <summary>发送数据</summary>
        Task<bool> SendAsync(byte[] data, CancellationToken cancellationToken = default);

        /// <summary>发送数据并等待回复</summary>
        Task<byte[]?> SendAndReceiveAsync(byte[] data, TimeSpan timeout, CancellationToken cancellationToken = default);

        /// <summary>数据接收事件</summary>
        event EventHandler<byte[]>? DataReceived;

        /// <summary>连接状态变更事件</summary>
        event EventHandler<ConnectionState>? StateChanged;
    }

    /// <summary>
    /// TCP服务端接口 — 监听客户端连接。
    /// </summary>
    public interface ITcpServer : IDisposable
    {
        /// <summary>启动监听</summary>
        Task StartAsync(int port, CancellationToken cancellationToken = default);

        /// <summary>停止监听</summary>
        Task StopAsync();

        /// <summary>是否正在监听</summary>
        bool IsListening { get; }

        /// <summary>当前连接的客户端数量</summary>
        int ClientCount { get; }

        /// <summary>向所有客户端广播数据</summary>
        Task BroadcastAsync(byte[] data);

        /// <summary>新客户端连接事件</summary>
        event EventHandler<string>? ClientConnected;

        /// <summary>客户端断开事件</summary>
        event EventHandler<string>? ClientDisconnected;

        /// <summary>收到客户端数据事件</summary>
        event EventHandler<(string ClientId, byte[] Data)>? DataReceived;
    }

    // =========================================
    // SECS/GEM接口
    // =========================================

    /// <summary>
    /// SECS/GEM服务接口 — 半导体设备通信标准的完整抽象。
    /// 设计说明：
    ///   SECS/GEM是半导体工厂自动化的核心协议。
    ///   Host（工厂MES系统）通过此协议控制和监控Equipment（设备）。
    ///   此接口封装了HSMS传输层 + SECS-II消息层 + GEM合规层。
    /// </summary>
    public interface ISecsGemService : IDisposable
    {
        /// <summary>HSMS连接状态</summary>
        HsmsConnectionState ConnectionState { get; }

        /// <summary>GEM控制状态</summary>
        GemControlState ControlState { get; }

        /// <summary>GEM通讯状态</summary>
        GemCommunicationState CommunicationState { get; }

        /// <summary>连接到Host</summary>
        Task<bool> ConnectAsync(string host, int port);

        /// <summary>断开连接</summary>
        Task DisconnectAsync();

        /// <summary>发送SECS消息并等待回复</summary>
        Task<SecsMessageData?> SendAsync(int stream, int function, byte[] body, bool wantReply = true);

        /// <summary>注册消息处理器 — 收到指定SxFy时自动调用</summary>
        void RegisterHandler(int stream, int function, Func<byte[], Task<byte[]>> handler);

        /// <summary>请求上线</summary>
        Task<bool> GoOnlineAsync(bool remoteMode = true);

        /// <summary>请求离线</summary>
        Task GoOfflineAsync();

        /// <summary>触发采集事件（自动发送S6F11）</summary>
        Task TriggerEventAsync(uint eventId, Dictionary<string, object>? data = null);

        /// <summary>设置报警（发送S5F1）</summary>
        Task SetAlarmAsync(uint alarmId, string text);

        /// <summary>清除报警</summary>
        Task ClearAlarmAsync(uint alarmId);

        /// <summary>消息接收事件</summary>
        event EventHandler<SecsMessageData>? MessageReceived;

        /// <summary>控制状态变更事件</summary>
        event EventHandler<GemControlState>? ControlStateChanged;
    }

    /// <summary>
    /// SECS消息数据 — 简化的消息模型。
    /// </summary>
    public class SecsMessageData
    {
        /// <summary>Stream号</summary>
        public int Stream { get; init; }

        /// <summary>Function号</summary>
        public int Function { get; init; }

        /// <summary>是否需要回复</summary>
        public bool WantReply { get; init; }

        /// <summary>消息体（SECS-II编码的二进制数据）</summary>
        public byte[] Body { get; init; } = Array.Empty<byte>();

        /// <summary>系统字节（事务标识）</summary>
        public uint SystemBytes { get; init; }
    }

    // =========================================
    // 运动控制接口
    // =========================================

    /// <summary>
    /// 单轴控制接口 — 定义单个运动轴的所有操作。
    /// 每张运动控制卡的驱动实现此接口。
    /// </summary>
    public interface IAxisController
    {
        /// <summary>轴配置</summary>
        AxisConfig Config { get; }

        /// <summary>轴实时状态</summary>
        AxisStatus Status { get; }

        /// <summary>使能伺服</summary>
        Task<bool> ServoOnAsync();

        /// <summary>关闭伺服</summary>
        Task<bool> ServoOffAsync();

        /// <summary>绝对定位运动</summary>
        Task<bool> MoveAbsoluteAsync(double position, double velocity, double acceleration, double deceleration, CancellationToken cancellationToken = default);

        /// <summary>相对定位运动</summary>
        Task<bool> MoveRelativeAsync(double distance, double velocity, double acceleration, double deceleration, CancellationToken cancellationToken = default);

        /// <summary>JOG运动（持续运动直到调用Stop）</summary>
        Task<bool> JogAsync(double velocity, bool positive);

        /// <summary>回原点</summary>
        Task<bool> HomeAsync(CancellationToken cancellationToken = default);

        /// <summary>立即停止</summary>
        Task StopAsync();

        /// <summary>急停（紧急停止，减速度为最大值）</summary>
        Task EmergencyStopAsync();

        /// <summary>清除报警</summary>
        Task ClearAlarmAsync();

        /// <summary>等待运动完成</summary>
        Task<bool> WaitForDoneAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

        /// <summary>刷新轴状态</summary>
        Task RefreshStatusAsync();
    }

    /// <summary>
    /// 运动控制卡接口 — 抽象一张物理运动控制卡。
    /// 不同品牌的控制卡（固高、雷赛、ACS等）实现此接口。
    /// </summary>
    public interface IMotionCard : IDisposable
    {
        /// <summary>控制卡ID</summary>
        int CardId { get; }

        /// <summary>控制卡类型</summary>
        MotionCardType CardType { get; }

        /// <summary>支持的最大轴数</summary>
        int MaxAxisCount { get; }

        /// <summary>初始化控制卡</summary>
        Task<bool> InitializeAsync();

        /// <summary>关闭控制卡</summary>
        Task CloseAsync();

        /// <summary>获取指定轴的控制器</summary>
        IAxisController GetAxis(int axisIndex);

        /// <summary>直线插补运动（多轴同时到达目标位置）</summary>
        Task<bool> LinearMoveAsync(int[] axisIndices, double[] positions, double velocity, CancellationToken cancellationToken = default);

        /// <summary>圆弧插补运动</summary>
        Task<bool> ArcMoveAsync(int axisX, int axisY, double centerX, double centerY, double endX, double endY, double velocity, bool clockwise, CancellationToken cancellationToken = default);

        /// <summary>全部轴急停</summary>
        Task EmergencyStopAllAsync();
    }

    // =========================================
    // 视觉系统接口
    // =========================================

    /// <summary>
    /// 相机接口 — 抽象一台工业相机的操作。
    /// </summary>
    public interface ICamera : IDisposable
    {
        /// <summary>相机配置</summary>
        CameraConfig Config { get; }

        /// <summary>是否已打开</summary>
        bool IsOpened { get; }

        /// <summary>打开相机</summary>
        Task<bool> OpenAsync();

        /// <summary>关闭相机</summary>
        Task CloseAsync();

        /// <summary>采集一帧图像</summary>
        Task<byte[]?> CaptureAsync(CancellationToken cancellationToken = default);

        /// <summary>设置曝光时间（微秒）</summary>
        Task SetExposureAsync(double exposureUs);

        /// <summary>设置增益</summary>
        Task SetGainAsync(double gain);

        /// <summary>设置触发模式</summary>
        Task SetTriggerModeAsync(TriggerMode mode);

        /// <summary>图像采集事件</summary>
        event EventHandler<byte[]>? ImageCaptured;
    }

    /// <summary>
    /// 视觉引擎接口 — 抽象视觉处理算法。
    /// Halcon、OpenCV、VisionPro等都实现此接口。
    /// </summary>
    public interface IVisionEngine : IDisposable
    {
        /// <summary>引擎类型</summary>
        VisionEngineType EngineType { get; }

        /// <summary>是否已初始化</summary>
        bool IsInitialized { get; }

        /// <summary>初始化引擎</summary>
        Task<bool> InitializeAsync();

        /// <summary>模板匹配 — 在图像中查找模板的位置和角度</summary>
        Task<VisionResult> TemplateMatchAsync(byte[] image, int width, int height, object templateData);

        /// <summary>Blob分析 — 二值化后提取连通区域特征</summary>
        Task<VisionResult> BlobAnalysisAsync(byte[] image, int width, int height, double threshold);

        /// <summary>OCR识别 — 识别图像中的文字</summary>
        Task<VisionResult> OcrAsync(byte[] image, int width, int height, object ocrModel);

        /// <summary>Mark点定位 — 查找标记点位置用于对位</summary>
        Task<VisionResult> FindMarkAsync(byte[] image, int width, int height);

        /// <summary>尺寸测量 — 测量图像中的距离/角度</summary>
        Task<VisionResult> MeasureAsync(byte[] image, int width, int height, object measureConfig);

        /// <summary>缺陷检测 — 检测产品表面缺陷</summary>
        Task<VisionResult> DefectDetectAsync(byte[] image, int width, int height, object detectConfig);
    }

    // =========================================
    // 应用层接口
    // =========================================

    /// <summary>
    /// 报警服务接口。
    /// </summary>
    public interface IAlarmService
    {
        /// <summary>当前活跃报警列表</summary>
        IReadOnlyList<AlarmRecord> ActiveAlarms { get; }

        /// <summary>触发报警</summary>
        void TriggerAlarm(int alarmCode, AlarmLevel level, string message, string source);

        /// <summary>清除指定报警</summary>
        void ClearAlarm(int alarmCode, string clearedBy);

        /// <summary>一键清除所有报警</summary>
        void ClearAllAlarms(string clearedBy);

        /// <summary>查询报警历史</summary>
        Task<List<AlarmRecord>> GetHistoryAsync(DateTime from, DateTime to);

        /// <summary>报警触发事件</summary>
        event EventHandler<AlarmRecord>? AlarmTriggered;

        /// <summary>报警清除事件</summary>
        event EventHandler<int>? AlarmCleared;
    }

    /// <summary>
    /// 配方管理接口。
    /// </summary>
    public interface IRecipeService
    {
        /// <summary>当前配方</summary>
        RecipeData? CurrentRecipe { get; }

        /// <summary>加载配方</summary>
        Task<RecipeData?> LoadAsync(string name);

        /// <summary>保存配方</summary>
        Task<bool> SaveAsync(RecipeData recipe);

        /// <summary>删除配方</summary>
        Task<bool> DeleteAsync(string name);

        /// <summary>获取所有配方名称</summary>
        Task<List<string>> GetRecipeListAsync();

        /// <summary>切换当前配方</summary>
        Task<bool> SwitchRecipeAsync(string name);

        /// <summary>配方切换事件</summary>
        event EventHandler<string>? RecipeChanged;
    }

    /// <summary>
    /// 用户管理接口。
    /// </summary>
    public interface IUserService
    {
        /// <summary>当前登录用户</summary>
        UserInfo? CurrentUser { get; }

        /// <summary>用户登录</summary>
        Task<bool> LoginAsync(string username, string password);

        /// <summary>用户登出</summary>
        void Logout();

        /// <summary>检查权限</summary>
        bool HasPermission(UserRole requiredRole);

        /// <summary>登录状态变更事件</summary>
        event EventHandler<UserInfo?>? UserChanged;
    }

    /// <summary>
    /// 日志服务接口 — UI日志面板使用。
    /// </summary>
    public interface ILogService
    {
        /// <summary>写入日志</summary>
        void Log(Enums.LogLevel level, string source, string message, string? traceId = null);

        /// <summary>获取最新日志</summary>
        IReadOnlyList<LogEntry> GetRecentLogs(int count = 100);

        /// <summary>新日志写入事件</summary>
        event EventHandler<LogEntry>? LogAdded;
    }
}
