/**
 * @file connectionstate.cpp
 * @brief V3 领域层 —— 连接状态机与重连退避策略实现
 *
 * ── 开发思路 ──
 * 三个自由函数都是纯函数（无状态、无副作用），各自集中一处业务规则：
 *   canTransition   —— 状态转移白名单；
 *   connectionStateName —— 状态 → 中文展示文本；
 *   nextRetryDelayMs —— 重连等待时间（指数退避，封顶）。
 */

#include "domain/connectionstate.h"

namespace dscope {
namespace domain {

bool canTransition(ConnectionState from, ConnectionState to)
{
    // 只列"允许"的转移，其余一律拒绝（默认拒绝比默认放行更安全）。
    switch (from) {
    case ConnectionState::Disconnected:
        return to == ConnectionState::Connecting;                      // 发起连接
    case ConnectionState::Connecting:
        return to == ConnectionState::Connected
            || to == ConnectionState::Error;                           // 成功或失败
    case ConnectionState::Connected:
        return to == ConnectionState::Error
            || to == ConnectionState::Disconnected;                    // 断线或主动断开
    case ConnectionState::Error:
        return to == ConnectionState::Connecting
            || to == ConnectionState::Disconnected;                    // 重试或放弃
    }

    // 防御分支：理论上不可达（from 已被枚举覆盖），保证任何强转值都返回 false
    return false;
}

QString connectionStateName(ConnectionState state)
{
    switch (state) {
    case ConnectionState::Disconnected:
        return QStringLiteral("未连接");
    case ConnectionState::Connecting:
        return QStringLiteral("连接中");
    case ConnectionState::Connected:
        return QStringLiteral("已连接");
    case ConnectionState::Error:
        return QStringLiteral("错误");
    }

    return QStringLiteral("未知");
}

int nextRetryDelayMs(int attempt)
{
    // 负次数的防御：视为第一次重试，返回基础间隔
    if (attempt < 0)
        attempt = 0;

    // 封顶：等待时间上限 30 秒，避免指数增长到不可接受的长时间
    const qint64 kMaxDelayMs = 30000;

    // 钳制：attempt >= 31 时 1000<<attempt 会溢出 qint64 符号位（有符号左移是 UB）。
    // 而 1000<<30 已是 ~1.07e12 ms，远超 30s 封顶，故 attempt>=31 直接返回封顶值，
    // 既保证结果正确，又规避"移位位数越界/符号溢出"的未定义行为。
    if (attempt >= 31)
        return static_cast<int>(kMaxDelayMs);

    // 指数退避：基础 1000ms，每失败一次翻倍（1000 → 2000 → 4000 → 8000 → ...）
    // 用 qint64 承载左移结果，避免 attempt 过大时 int 溢出（虽然会被封顶拦下）。
    qint64 delay = static_cast<qint64>(1000) << attempt;

    if (delay > kMaxDelayMs)
        delay = kMaxDelayMs;

    return static_cast<int>(delay);
}

} // namespace domain
} // namespace dscope
