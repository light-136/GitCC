/**
 * @file domainmodel_v2.h
 * @brief DataScope Studio V2 领域模型（真实工程级）
 *
 * ────────────────────────────────────────────────────────────
 * 为什么要有这一层（V2 核心补课，对应审查 P0-1"领域层只是被动 DTO"）
 * ────────────────────────────────────────────────────────────
 * V1 的 domain/models.h 只有 DeviceStatus/Channel/DataPoint/Device 4 个被动
 * 数据结构：无行为、无业务不变量、无生命周期，任何"校验"都要调用方自己做，
 * 结果就是量程/报警/状态转移这些业务逻辑散落在 UI(View) 和 Service 里。
 *
 * V2 领域模型的目标：
 *   1. 类型即文档 —— 告警规则、采集会话、连接参数这些"工业监控软件一定有的
 *      概念"在 V1 里根本没有建模，V2 逐一补齐；
 *   2. 不变量内聚 —— 量程是否合法、状态能否转移、报警能否确认，由领域类
 *      自己保证，调用方不可能把系统推进非法状态（对应 WPF 中把校验放进
 *      Domain Entity / ViewModel 的做法）；
 *   3. Value vs Entity 分清 —— 值类型（可拷贝、无身份）用 value class，
 *      实体（有身份、有生命周期、有状态）用 class；
 *   4. 领域零依赖 —— 本层只依赖 QtCore（QString/QDateTime/QVector），
 *      不依赖 UI、QtNetwork、QtSerialPort，任何界面或协议改动都不波及这里。
 *
 * ── WPF 对照 ──
 *   DeviceId          ↔  记录主键 / Guid（不可变标识）
 *   DeviceState       ↔  设备连接状态枚举（含合法转移）
 *   ConnectionParams  ↔  连接配置 DTO（但带 IsValid 校验）
 *   ChannelConfig     ↔  通道参数（含量程/单位换算）
 *   Channel           ↔  IObservableChannel（有当前值 + 状态）
 *   DataPoint         ↔  采样样本（含质量戳 Quality）
 *   AlarmRule/AlarmEvent ↔  报警规则 / 报警事件（INotifyPropertyChanged）
 *   AcquisitionRecord ↔  采集会话记录（Session）
 *
 * ── 为什么命名空间 v2 ──
 *   工程演进遵循"先保运行、再逐步迁移"：V2 类型与 V1 的 Channel/DataPoint 同名
 *   但语义不同，放独立命名空间避免符号冲突，旧代码继续编译运行；
 *   阶段 D/E 接线完成后，V1 类型由 UI 层逐步切换到 v2。
 */

#pragma once

#include <QDateTime>
#include <QString>
#include <QVector>
#include <QtGlobal>

// 将 AlarmEvent 注册为 Qt 元类型：AlarmEngine 的信号以它为参数跨线程/跨队列传递
// 时必须可被元对象系统识别（对应审查发现"ProtocolFrame 未注册元类型"的预防）
// 注意：Q_DECLARE_METATYPE 要求类型有 public 默认构造、拷贝构造、析构 —— 均满足
#include <QMetaType>

namespace datascope {
namespace domain {
namespace v2 {

/* ============================================================================
 * 一、DeviceId —— 设备唯一标识（值类型，不可变）
 * ============================================================================
 * 设计思路：设备被"创建/编辑/删除/复制"后仍然需要稳定地定位它，
 * 用自增整数在删除重建后会被复用（历史数据错乱），所以用随机 UUID。
 * value class：拷贝即值拷贝，无身份概念 —— 两个同 id 的对象是"同一个设备"。
 */
class DeviceId
{
public:
    /** @brief 默认构造：无效 id（表示"尚未分配"） */
    DeviceId() = default;

    /** @brief 从字符串构造（内部不做校验，见 isValid()） */
    explicit DeviceId(const QString &value) : m_value(value) {}

    /** @brief 生成一个新的随机 id */
    static DeviceId create();

    /** @brief id 文本（只读，不可变） */
    const QString &value() const { return m_value; }

    /** @brief 是否有效（非空） */
    bool isValid() const { return !m_value.isEmpty(); }

    /** @brief 相等比较：同 id 视为同一设备 */
    bool operator==(const DeviceId &other) const { return m_value == other.m_value; }
    bool operator!=(const DeviceId &other) const { return !(*this == other); }

private:
    QString m_value;   ///< id 文本（生成时带 "DEV-" 前缀便于日志辨识）
};

/* ============================================================================
 * 二、DeviceState —— 设备连接状态（枚举 + 合法转移表）
 * ============================================================================
 * 设计思路：连接状态不能随意跳转（如 Online 不能直接进 Connecting 之前的
 * Disconnected 之外的状态），用 canTransition(from, to) 把"合法转移表"集中
 * 到一处，任何调用方想推进状态都必须先问它 —— 状态机不变量内聚。
 * 对应 V1 只有 4 个状态的 DeviceStatus，V2 补齐 Connecting/Configuring 与
 * "用户主动断开 vs 意外断线"的区别。
 */
enum class DeviceState {
    Disconnected = 0,  ///< 未连接（初始状态）
    Connecting,        ///< 正在连接（异步进行中）
    Online,            ///< 已连接并可采集
    Configuring,       ///< 连接中但正在下发配置（可选）
    Error              ///< 出错（通信异常/设备离线，可重连）
};

/**
 * @brief 合法状态转移表
 * Disconnected → Connecting → Online；Online → Error（意外断线）；
 * Error → Connecting（重连）；Online/Error → Disconnected（用户主动断开）。
 */
bool canTransition(DeviceState from, DeviceState to);

/** @brief 状态显示名（中文，供 UI/日志使用） */
QString deviceStateName(DeviceState state);

/* ============================================================================
 * 三、ConnectionParams —— 设备连接参数（值类型，带校验）
 * ============================================================================
 * 设计思路：V1 把 IP/端口写死在 mainwindow.cpp，换设备就要改代码。
 * V2 把它建模成独立值类型，支持 TCP/串口两种连接方式，isValid() 保证
 * 只有"配置完整"的参数才能被使用 —— 对应 C# 中带 DataAnnotations 的 DTO。
 */
enum class ConnectionType {
    Tcp,    ///< TCP 网络连接（host + port）
    Serial  ///< 串口连接（portName + baudRate，P9 预留）
};

class ConnectionParams
{
public:
    ConnectionType type = ConnectionType::Tcp;   ///< 连接方式
    QString  host;                              ///< 主机地址（TCP）
    quint16  port = 0;                          ///< 端口（TCP）
    QString  portName;                          ///< 串口号（Serial，如 "COM3"）
    int      baudRate = 0;                      ///< 波特率（Serial）
    int      connectTimeoutMs = 5000;           ///< 连接超时（毫秒，默认 5s）

    /** @brief 参数是否合法（不完整则不可用） */
    bool isValid() const;
};

/* ============================================================================
 * 四、ChannelConfig —— 通道配置（值类型，带量程换算）
 * ============================================================================
 * 设计思路：V1 的 Channel 只有 index/name/unit/scale/offset，没有量程上下限，
 * "报警阈值"只能在 UI 里写死 m_max*0.85。V2 把量程(min/max)收进配置，
 * toEngineering() 负责原始码值 → 工程值的换算 —— 业务语义进领域，UI 只渲染。
 */
class ChannelConfig
{
public:
    int     index  = 0;      ///< 通道序号（0 基，与硬件通道号对应）
    QString name;            ///< 通道名（如 "温度"）
    QString unit;            ///< 工程单位（如 "℃"）
    double  scale  = 1.0;    ///< 缩放系数（原始值 × scale + offset）
    double  offset = 0.0;    ///< 偏移量
    double  minValue = 0.0;  ///< 量程下限（工程值）
    double  maxValue = 100.0;///< 量程上限（工程值）

    /** @brief 配置是否合法：量程下限必须 ≤ 上限 */
    bool isValid() const { return minValue <= maxValue; }

    /** @brief 原始码值 → 工程值（业务换算内聚，不允许 UI 各自算） */
    double toEngineering(double raw) const { return raw * scale + offset; }

    /** @brief 工程值是否越限（超上限/低于下限） */
    bool isOutOfRange(double engValue) const
    {
        return engValue > maxValue || engValue < minValue;
    }
};

/* ============================================================================
 * 五、DataQuality —— 数据质量（枚举）
 * ============================================================================
 * 设计思路：V1 的 DataPoint 只有 value，无法表达"这次采样是否可信"。
 * 真实监控软件必须区分"好数据"与"坏数据/越限数据"，否则报警判断会失真。
 */
enum class DataQuality {
    Good,        ///< 正常有效
    OverRange,   ///< 超上限（量程外）
    UnderRange,  ///< 低于下限（量程外）
    Invalid      ///< 无效（通信异常/解析失败）
};

/* ============================================================================
 * 六、DataPoint —— 一次采样数据点（值类型）
 * ============================================================================
 * 与 V1 的差别：新增 quality（质量戳）、raw（原始码值）、engValue（工程值），
 * 让"数值"与"数值是否可信"分离 —— 曲线控件可按 quality 换色、报警引擎按
 * quality 决定是否参与判断。
 */
struct DataPoint
{
    int        channelIndex = 0;   ///< 所属通道序号
    double     rawValue  = 0.0;    ///< 原始码值（未换算）
    double     engValue  = 0.0;    ///< 工程值（= raw × scale + offset）
    DataQuality quality  = DataQuality::Good; ///< 质量戳
    QDateTime  timestamp;          ///< 采样时刻（默认无效时间）

    /** @brief 便捷构造：直接给出全部字段 */
    DataPoint(int ch, double raw, double eng, DataQuality q, const QDateTime &ts)
        : channelIndex(ch), rawValue(raw), engValue(eng), quality(q), timestamp(ts) {}

    /** @brief 默认构造（供容器使用） */
    DataPoint() = default;
};

/* ============================================================================
 * 六.五、Device —— 设备聚合根（实体，有身份/状态/配置）
 * ============================================================================
 * 设计思路：V1 的 Device 是纯 getter/setter 容器，V2 把设备建模为聚合根：
 *   1. 有身份（DeviceId）、有状态（DeviceState）、有配置（ConnectionParams）、
 *      有通道集合（QVector<ChannelConfig>）；
 *   2. 状态转移必须过 canTransition() 校验 —— 调用方无法把设备推进非法状态
 *      （如未连接直接变"已连接"）；
 *   3. 连接参数生效前必须 isValid() —— 配置不完整不能进入 Online。
 * 这是"设备管理页 + 设备导航 + 状态展示"等 UI 的共同数据源（阶段 E 接线）。
 */
class Device
{
public:
    /** @brief 默认构造：一台未连接的空设备 */
    Device() = default;

    /** @brief 用 id 构造（名称默认空，随后 setName） */
    explicit Device(const DeviceId &id) : m_id(id) {}

    // ---- 只读 ----
    const DeviceId &id() const               { return m_id; }
    QString name() const                     { return m_name; }
    DeviceState state() const                { return m_state; }
    const ConnectionParams &connection() const { return m_conn; }
    const QVector<ChannelConfig> &channels() const { return m_channels; }
    int channelCount() const                 { return m_channels.size(); }

    // ---- 写操作（均带不变量校验） ----

    void setName(const QString &name)        { m_name = name; }

    /** @brief 设置连接参数（不校验；校验发生在连接动作处） */
    void setConnection(const ConnectionParams &params) { m_conn = params; }

    /**
     * @brief 推进状态（带合法转移校验）
     * @return true 转移成功；false 非法转移（状态不变）
     */
    bool transitionTo(DeviceState newState);

    /** @brief 添加通道配置（同 index 重复时覆盖，保证通道号唯一） */
    void upsertChannel(const ChannelConfig &cfg);

    /** @brief 移除指定 index 的通道 */
    bool removeChannel(int index);

    /** @brief 清空全部通道 */
    void clearChannels();

private:
    DeviceId   m_id;                 ///< 设备唯一标识
    QString    m_name;               ///< 设备名称
    DeviceState m_state = DeviceState::Disconnected; ///< 连接状态（初始未连接）
    ConnectionParams m_conn;         ///< 连接参数
    QVector<ChannelConfig> m_channels; ///< 通道配置集合
};

/* ============================================================================
 * 七、AlarmCondition / AlarmRule —— 报警规则（值类型）
 * ============================================================================
 * 设计思路：V1 的"报警"只是 monitorpage 里 value > max*0.85 一行内联代码，
 * 无法测试、无法复用。V2 把报警规则建模为可配置的领域对象：
 *   触发条件(condition) + 阈值(threshold) + 是否保持(latching)。
 * 规则本身不含"当前是否报警"——那由 AlarmEngine 结合实时值评估（阶段 D）。
 */
enum class AlarmCondition {
    AboveHigh,   ///< 超过上限值（工程值 > threshold）
    BelowLow,    ///< 低于下限值（工程值 < threshold）
    OutOfRange   ///< 越限（超出量程上下限）
};

class AlarmRule
{
public:
    int             channelIndex = 0; ///< 被监控的通道序号
    AlarmCondition  condition = AlarmCondition::AboveHigh; ///< 触发条件
    double          threshold = 0.0;  ///< 阈值（工程值）
    bool            latching  = true; ///< 是否保持：触发后保持报警直到手动确认（HMI 惯例）
    QString         description;      ///< 报警描述（如 "温度过高"）

    /**
     * @brief 评估：给定工程值，是否触发本规则
     * @return true 表示满足触发条件
     * @note 这是纯函数（不修改自身），供 AlarmEngine 每秒/每数据帧调用
     */
    bool isTriggered(double engValue) const;
};

/* ============================================================================
 * 八、AlarmSeverity / AlarmEvent —— 报警事件（实体，带生命周期）
 * ============================================================================
 * 设计思路：报警不是"一条布尔"，而是有生命的实体：产生(Active)→确认(Ack)→
 * 清除(Clear)。latching 规则触发后即使值恢复也必须人工确认，防漏报。
 * 状态机不变量由 AlarmEvent 自己保证（ack 只在 Active 态有意义）。
 */
enum class AlarmSeverity {
    Info,      ///< 提示（不影响运行）
    Warning,   ///< 警告（需关注）
    Critical   ///< 严重（需立即处理）
};

class AlarmEvent
{
public:
    AlarmEvent() = default;

    /** @brief 构造并立即进入 Active 状态 */
    AlarmEvent(quint64 id, int ruleIndex, int channelIndex,
               AlarmSeverity severity, const QString &message,
               const QDateTime &time);

    // ---- 只读 ----
    quint64 id() const           { return m_id; }
    int     ruleIndex() const    { return m_ruleIndex; }
    int     channelIndex() const { return m_channelIndex; }
    AlarmSeverity severity() const { return m_severity; }
    QString message() const      { return m_message; }
    QDateTime time() const       { return m_time; }
    QDateTime ackTime() const    { return m_ackTime; }
    bool isActive() const        { return m_active; }
    bool isAcked() const         { return m_acked; }

    /** @brief 确认（只能在 Active 态确认；重复确认是 no-op） */
    void ack(const QDateTime &ackTime);

    /** @brief 清除报警（解除高亮；确认后的清除是正常路径） */
    void clear();

private:
    quint64     m_id = 0;
    int         m_ruleIndex = -1;
    int         m_channelIndex = -1;
    AlarmSeverity m_severity = AlarmSeverity::Info;
    QString     m_message;
    QDateTime   m_time;      ///< 产生时刻
    QDateTime   m_ackTime;   ///< 确认时刻（未确认时无效）
    bool        m_active = true;  ///< 报警是否仍处于活动状态
    bool        m_acked  = false; ///< 是否已被确认
};

/* ============================================================================
 * 九、AcquisitionRecord —— 采集会话记录（实体）
 * ============================================================================
 * 设计思路：V1 的 RecordManager 直接写 CSV，无"会话"概念。V2 把一次记录
 * 建模为会话：有开始/结束时间、采样点数、被记录的通道元数据。
 * 这样回放/统计/校验都建立在"完整会话"之上，而不是裸文件流。
 */
class AcquisitionRecord
{
public:
    AcquisitionRecord() = default;

    /** @brief 开启一次新会话（自动记录开始时间） */
    static AcquisitionRecord begin(const QString &sessionId);

    // ---- 只读 ----
    QString sessionId() const    { return m_sessionId; }
    QDateTime startTime() const  { return m_startTime; }
    QDateTime endTime() const    { return m_endTime; }
    qint64 sampleCount() const   { return m_sampleCount; }
    bool isActive() const        { return m_active; }

    /** @brief 记录一个新采样（计数 +1） */
    void addSample();

    /** @brief 结束会话（设置 endTime 并标记非活动；重复结束是 no-op） */
    void end(const QDateTime &endTime);

private:
    QString   m_sessionId;
    QDateTime m_startTime;
    QDateTime m_endTime;
    qint64    m_sampleCount = 0;
    bool      m_active = false;
};

} // namespace v2
} // namespace domain
} // namespace datascope

// 元类型注册：AlarmEvent 必须能被 QMetaType 识别（信号参数/跨线程传递用）
// Q_DECLARE_METATYPE 要求参数在全局/父命名空间下完全限定 —— 此处已可解析
Q_DECLARE_METATYPE(datascope::domain::v2::AlarmEvent)
