/**
 * @file alarmengine.cpp
 * @brief V3 领域层 —— 报警引擎实现
 *
 * ── 开发思路 ──
 * 核心是 evaluate() 的"边沿检测"：用 m_active 记录每条规则上一时刻的报警态，
 * 只有状态发生翻转时才产出事件。事件时间戳统一取 dp.hostArrivalTs（主机收到
 * 帧的时刻），因为 V3 第一阶段 Simulator 帧内暂不含设备时间戳。
 */

#include "domain/alarmengine.h"

namespace dscope {
namespace domain {

int AlarmEngine::addRule(const AlarmRule &rule)
{
    m_rules.append(rule);      // 规则入队
    m_active.append(false);    // 同步追加初始状态：新规则一开始不处于报警态
    return m_rules.size() - 1; // 返回刚加入的规则的索引
}

void AlarmEngine::clearRules()
{
    m_rules.clear();
    m_active.clear();          // 状态随规则一并清空，避免出现"有状态无规则"的悬空
}

int AlarmEngine::ruleCount() const
{
    return m_rules.size();
}

bool AlarmEngine::isRuleActive(int ruleIndex) const
{
    // 越界防御：查询一个不存在的规则索引时返回 false，而不是触发越界崩溃
    if (ruleIndex < 0 || ruleIndex >= m_active.size())
        return false;
    return m_active.at(ruleIndex);
}

QVector<AlarmEvent> AlarmEngine::evaluate(const DataPoint &dp)
{
    QVector<AlarmEvent> events;

    // 遍历全部规则，只评估通道号匹配的那些
    for (int i = 0; i < m_rules.size(); ++i) {
        const AlarmRule &rule = m_rules.at(i);
        if (rule.channelIndex != dp.channelIndex)
            continue;   // 别的通道的规则与本数据点无关，跳过

        const bool active    = m_active.at(i);             // 上一时刻是否已在报警态
        const bool triggered = rule.isTriggered(dp.value); // 是否越过触发边界
        const bool recovered = rule.isRecovered(dp.value); // 是否越过恢复边界

        if (!active && triggered) {
            // 边沿①：未报警 → 报警（触发）
            m_active[i] = true;

            AlarmEvent ev;
            ev.type         = AlarmEvent::Trigger;
            ev.channelIndex = dp.channelIndex;
            ev.ts           = dp.hostArrivalTs;   // 事件时间戳 = 主机收到帧的时刻
            events.append(ev);
        } else if (active && recovered) {
            // 边沿②：报警 → 正常（恢复）
            m_active[i] = false;

            AlarmEvent ev;
            ev.type         = AlarmEvent::Recover;
            ev.channelIndex = dp.channelIndex;
            ev.ts           = dp.hostArrivalTs;
            events.append(ev);
        }
        // 其余两种情况不产出事件：
        //   !active && !triggered  —— 还没触发，继续观察
        //   active  && !recovered  —— 还在报警态（含回差死区内），保持不刷屏
    }

    return events;
}

} // namespace domain
} // namespace dscope
