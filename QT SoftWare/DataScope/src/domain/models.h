/**
 * @file models.h
 * @brief 领域模型（P8 Domain 层）：设备 / 通道 / 数据点
 *
 * 本文件是 DataScope Studio 的"纯数据模型"层（对应 WPF/C# 的 DTO/POCO）。
 * 设计红线：只依赖 QtCore（QString / QDateTime / QVector），
 * 不依赖 UI、不依赖 QtNetwork / SerialPort——任何界面或协议改动都不应波及这里。
 *
 * 设计总览（为什么这样划分类型）：
 *   1. Channel / DataPoint 用 struct —— 它们是"值类型"（对应 C# 的 record / POCO）：
 *      - 可直接拷贝、赋值、放进容器（QVector），拷贝后各持一份，互不影响；
 *      - 全部成员公有、带默认值，构造/序列化都方便，最贴合"一次性采样/通道描述"这种轻量数据。
 *   2. Device 用 class —— 它"拥有并管理"一组 Channel，与自身状态（id/name/status）内聚：
 *      - 对外只暴露只读通道列表与少量写操作，防止外部随意篡改内部数组（封装/内聚）；
 *      - 对应 C# 的 class Device { public IReadOnlyList<Channel> Channels { get; } }。
 *   3. enum class DeviceStatus —— C++11 强类型枚举：
 *      - 不会隐式转成整数（C# 的 enum 本质仍是 int，可与整数随意互转，容易写出脏数据）；
 *      - 必须显式写 DeviceStatus::Connected 才能使用，编译期即可挡住大量笔误。
 *
 * 教学点（阅读时注意）：
 *   1. `const QVector<Channel> &channels() const` 返回"只读引用"：
 *      调用方拿到的是内部数组的一份只读视图，而不是拷贝——
 *      既避免了每次读取都复制整个通道数组（性能），又保证只读安全（对应 C# 的 IReadOnlyList<T>）。
 *   2. QVector 与 QString 一样是"隐式共享"（Copy-on-Write）容器：
 *      拷贝一个 Device 非常便宜（共享底层数据），只有真正写入时才复制底层数组（见 models.cpp 说明）。
 *   3. 头文件里只声明、不定义成员函数体（除 =default 的默认构造），
 *      这样每个 include 本头的 .cpp 都不必重复编译实现，加快整体构建。
 */

#pragma once

#include <QDateTime>
#include <QString>
#include <QtGlobal>
#include <QVector>

namespace datascope {
namespace domain {

/**
 * @enum DeviceStatus
 * @brief 设备连接状态（强类型枚举，显式给整数值便于持久化/日志输出）
 *
 * 状态机演化方向：Disconnected → Connecting → Connected；出错时进 Error。
 * 默认值是 Disconnected（见 Device 成员 m_status 的初始化）。
 */
enum class DeviceStatus {
    Disconnected = 0,   ///< 未连接（初始状态）
    Connecting,         ///< 正在连接（异步过程中）
    Connected,          ///< 已连接（采集可用）
    Error               ///< 出错（通信异常/设备离线）
};

/**
 * @struct Channel
 * @brief 采集通道的描述信息（值类型，可拷贝）
 *
 * 对应一条硬件采集通道（如 4-20mA 的 1 号通道），是"静态配置"，不是采样数据。
 * 成员含义：
 *   - scale / offset 用于把原始码值换算为工程值：工程值 = 原始值 × scale + offset；
 *   - 用 struct 的原因：值语义、可直接整体拷贝/赋值，符合"通道描述"这种 POCO 的定位。
 */
struct Channel {
    int     index  = 0;   ///< 通道序号（0 基，与硬件通道号对应）
    QString name;         ///< 通道名（如 "温度"、"压力"）
    QString unit;         ///< 工程单位（如 "℃"、"kPa"）
    double  scale  = 1.0; ///< 缩放系数（原始值 × scale + offset）
    double  offset = 0.0; ///< 偏移量
};

/**
 * @struct DataPoint
 * @brief 一次采样数据点（值类型，可拷贝）
 *
 * 对应"某通道在某时刻采样到的值"，是"动态数据"，会高频产生、批量进入环形缓冲。
 * 用 struct 且全成员公有，方便无拷贝地聚合到 QVector 里做批量处理。
 */
struct DataPoint {
    int       channelIndex = 0;   ///< 所属通道序号（对应 Channel::index）
    double    value        = 0.0; ///< 采样值（已完成 scale/offset 换算的工程值）
    QDateTime timestamp;          ///< 采样时刻（默认构造为"无效时间"，见单测校验）
};

/**
 * @class Device
 * @brief 采集设备（一组通道 + 状态，class 管理内聚）
 *
 * 用 class 而不是 struct 的原因：
 *   - Device 内部持有"通道集合"这一资源，需要对外提供"只读视图"并自己维护写入入口，
 *     这正是 class 的封装职责；struct 的"全公有、裸数据"定位不适合承载集合管理。
 *   - 拷贝安全：Device 可拷贝（默认拷贝构造），且因为 QVector 隐式共享，
 *     拷贝副本的修改不会影响原对象（见单测 device_copyIndependent）。
 */
class Device
{
public:
    // 默认构造：等价于"新建一台未连接的空设备"（状态/通道数见单测 defaultState）
    Device() = default;

    // ---- 只读访问 ----

    /** @brief 设备唯一标识（如 "DEV-001"） */
    QString id() const;

    /** @brief 设备名称（如 "温度采集仪"） */
    QString name() const;

    /** @brief 当前连接状态 */
    DeviceStatus status() const;

    /** @brief 通道数量 */
    int channelCount() const;

    /**
     * @brief 全部通道（只读引用）
     * @return 内部通道数组的 const 引用；调用方不可修改，且不产生拷贝
     * @note 对应 C# 的 IReadOnlyList<T>：用引用避免每次读取都复制整个数组。
     *       因为返回的是引用，调用方必须小心：不要在 Device 被销毁/改写后继续持有。
     */
    const QVector<Channel> &channels() const;

    // ---- 写操作 ----

    /** @brief 设置设备唯一标识 */
    void setId(const QString &id);

    /** @brief 设置设备名称 */
    void setName(const QString &name);

    /** @brief 设置连接状态（由连接管理逻辑调用，如 Connecting → Connected） */
    void setStatus(DeviceStatus status);

    /** @brief 追加一个采集通道（调用方负责保证不重复，本类不做过多的业务校验） */
    void addChannel(const Channel &ch);

    /** @brief 清空全部通道（重新配置设备时使用） */
    void clearChannels();

private:
    QString     m_id;                          ///< 设备唯一标识
    QString     m_name;                        ///< 设备名称
    DeviceStatus m_status = DeviceStatus::Disconnected; ///< 连接状态（默认未连接）
    QVector<Channel> m_channels;               ///< 通道集合
};

} // namespace domain
} // namespace datascope
