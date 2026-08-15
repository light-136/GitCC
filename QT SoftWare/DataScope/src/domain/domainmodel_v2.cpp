/**
 * @file domainmodel_v2.cpp
 * @brief DataScope Studio V2 领域模型实现
 *
 * 本文件只包含"有逻辑"的成员函数定义；纯 getter 已在头文件内联。
 * 设计思路（对应审查"领域模型要有行为"）：
 *   - DeviceId::create      —— 生成唯一标识（行为，非纯存储）
 *   - canTransition         —— 状态转移表（状态机不变量集中管理）
 *   - ConnectionParams::isValid —— 参数校验（不完整则不可用）
 *   - AlarmRule::isTriggered—— 报警条件评估（纯函数，可单测）
 *   - AlarmEvent 生命周期    —— Active→Ack→Clear 状态机
 *   - AcquisitionRecord      —— 会话完整性（begin/add/end）
 *
 * 与 V1 的关系：这是 V2 新增文件，命名空间 datascope::domain::v2，
 * 与 V1 的 models.h 并存不冲突。阶段 D/E 接线后由 UI/Service 层逐步使用。
 */

#include "domain/domainmodel_v2.h"

#include <QUuid>

namespace datascope {
namespace domain {
namespace v2 {

/* ==================== DeviceId ==================== */

DeviceId DeviceId::create()
{
    // QUuid::createUuid() 生成 128 位随机 UUID，取去除花括号的紧凑形式
    // 前缀 "DEV-" 仅用于日志/调试辨识，不参与身份语义
    const QString raw = QUuid::createUuid().toString(QUuid::WithoutBraces);
    return DeviceId(QStringLiteral("DEV-") + raw);
}

/* ==================== DeviceState 状态转移 ==================== */

bool canTransition(DeviceState from, DeviceState to)
{
    switch (from) {
    case DeviceState::Disconnected:
        // 未连接 -> 只能去"连接中"或继续未连接
        return to == DeviceState::Connecting;

    case DeviceState::Connecting:
        // 连接中 -> 连接成功(Online) / 配置下发(Configuring) / 失败(Error)
        return to == DeviceState::Online
            || to == DeviceState::Configuring
            || to == DeviceState::Error;

    case DeviceState::Online:
        // 已连接 -> 意外断线(Error) / 用户主动断开(Disconnected)
        return to == DeviceState::Error
            || to == DeviceState::Disconnected;

    case DeviceState::Configuring:
        // 配置中 -> 配置完成(Online) / 配置失败(Error)
        return to == DeviceState::Online
            || to == DeviceState::Error;

    case DeviceState::Error:
        // 出错 -> 自动重连(Connecting) / 用户手动断开(Disconnected)
        return to == DeviceState::Connecting
            || to == DeviceState::Disconnected;
    }
    return false;
}

QString deviceStateName(DeviceState state)
{
    switch (state) {
    case DeviceState::Disconnected: return QStringLiteral("未连接");
    case DeviceState::Connecting:   return QStringLiteral("连接中");
    case DeviceState::Online:       return QStringLiteral("已连接");
    case DeviceState::Configuring:  return QStringLiteral("配置中");
    case DeviceState::Error:        return QStringLiteral("出错");
    }
    return QStringLiteral("未知");
}

/* ==================== ConnectionParams ==================== */

bool ConnectionParams::isValid() const
{
    switch (type) {
    case ConnectionType::Tcp:
        // TCP：主机地址非空、端口在有效范围、超时为正
        return !host.isEmpty()
            && port > 0 && port <= 65535
            && connectTimeoutMs > 0;

    case ConnectionType::Serial:
        // 串口：串口号非空、波特率为正（波特率取值范围由系统决定）
        return !portName.isEmpty()
            && baudRate > 0;
    }
    return false;
}

/* ==================== AlarmRule ==================== */

bool AlarmRule::isTriggered(double engValue) const
{
    switch (condition) {
    case AlarmCondition::AboveHigh:
        return engValue > threshold;
    case AlarmCondition::BelowLow:
        return engValue < threshold;
    case AlarmCondition::OutOfRange:
        // OutOfRange 用阈值对上/下限；此处用单阈值语义：超过阈值或低于 -阈值
        // 实际工程中可把 min/max 也放进规则，此处保持简洁
        return engValue > threshold || engValue < -threshold;
    }
    return false;
}

/* ==================== AlarmEvent 生命周期 ==================== */

AlarmEvent::AlarmEvent(quint64 id, int ruleIndex, int channelIndex,
                       AlarmSeverity severity, const QString &message,
                       const QDateTime &time)
    : m_id(id)
    , m_ruleIndex(ruleIndex)
    , m_channelIndex(channelIndex)
    , m_severity(severity)
    , m_message(message)
    , m_time(time)
{
    // 构造即进入 Active 状态（m_active 默认 true）
}

void AlarmEvent::ack(const QDateTime &ackTime)
{
    // 重复确认是 no-op（幂等）：
    //   已确认过则保留第一次确认时刻，避免"迟到确认"覆盖原始记录
    //   非活动状态（已清除）也不能再确认
    if (!m_active || m_acked)
        return;
    m_acked = true;
    m_ackTime = ackTime;
}

void AlarmEvent::clear()
{
    // 清除报警：无论是否确认过，只要活动就解除活动态
    if (m_active) {
        m_active = false;
        if (!m_acked) {
            // 未确认就清除：视为自动确认（记录清除时刻），避免"永远挂起"
            m_acked = true;
            m_ackTime = m_time;
        }
    }
}

/* ==================== AcquisitionRecord ==================== */

AcquisitionRecord AcquisitionRecord::begin(const QString &sessionId)
{
    AcquisitionRecord rec;
    rec.m_sessionId  = sessionId;
    rec.m_startTime  = QDateTime::currentDateTime();
    rec.m_sampleCount = 0;
    rec.m_active     = true;
    return rec;
}

void AcquisitionRecord::addSample()
{
    // 会话未结束时计数才有意义；结束后 addSample 是 no-op
    if (m_active)
        ++m_sampleCount;
}

void AcquisitionRecord::end(const QDateTime &endTime)
{
    // 重复结束是 no-op：保留第一次结束时刻
    if (!m_active)
        return;
    m_endTime = endTime;
    m_active  = false;
}

} // namespace v2
} // namespace domain
} // namespace datascope
