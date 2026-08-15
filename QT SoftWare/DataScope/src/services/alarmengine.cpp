/**
 * @file alarmengine.cpp
 * @brief 报警引擎实现
 *
 * 核心算法（去重 + 边界触发）：
 *   - 每个规则维护 m_ruleActive[i]：当前是否处于报警态；
 *   - evaluate() 时若 规则触发且之前未报警 → 产生新事件（边界触发）；
 *   - 若 规则未触发且之前报警中 → 发"恢复"信号（值回到正常范围）；
 *   - 若 状态未变（一直触发或一直正常）→ 什么都不做（去重）。
 *
 * 为什么必须去重：
 *   数据帧每 50ms 来一次，若每次触发都报警，报警中心会被刷屏。
 *   工业监控惯例是"报警只在进入条件时产生一次，恢复时清除/记录一次"。
 *
 * 为什么坏数据不评估：
 *   质量非 Good（超量程/无效/低于下限）的数据不能代表真实工况，
 *   用它们判断报警会报假警或漏报，所以直接跳过。
 */

#include "services/alarmengine.h"

#include <QDateTime>

namespace datascope {
namespace services {

AlarmEngine::AlarmEngine(QObject *parent)
    : QObject(parent)
{
}

int AlarmEngine::addRule(const datascope::domain::v2::AlarmRule &rule)
{
    const int idx = m_rules.size();
    m_rules.append(rule);
    m_ruleActive.append(false);  // 新规则初始未触发
    return idx;
}

void AlarmEngine::clearRules()
{
    m_rules.clear();
    m_ruleActive.clear();
}

bool AlarmEngine::isRuleActive(int ruleIndex) const
{
    if (ruleIndex < 0 || ruleIndex >= m_ruleActive.size())
        return false;
    return m_ruleActive.at(ruleIndex);
}

void AlarmEngine::evaluate(const datascope::domain::v2::DataPoint &dp)
{
    // 坏数据不参与报警判定（防假警）
    if (dp.quality != datascope::domain::v2::DataQuality::Good)
        return;

    for (int i = 0; i < m_rules.size(); ++i) {
        const datascope::domain::v2::AlarmRule &rule = m_rules.at(i);
        if (rule.channelIndex != dp.channelIndex)
            continue;  // 只评估匹配当前通道的规则

        const bool triggered = rule.isTriggered(dp.engValue);
        const bool wasActive = m_ruleActive.at(i);

        if (triggered && !wasActive) {
            // 边界触发：产生新报警事件
            m_ruleActive[i] = true;
            datascope::domain::v2::AlarmEvent event(
                m_nextEventId++, i, rule.channelIndex,
                datascope::domain::v2::AlarmSeverity::Warning,
                rule.description.isEmpty()
                    ? QStringLiteral("通道%1 报警").arg(rule.channelIndex + 1)
                    : rule.description,
                QDateTime::currentDateTime());
            emit ruleTriggered(event);
        }
        else if (!triggered && wasActive) {
            // 恢复：值回到正常范围
            m_ruleActive[i] = false;
            emit ruleRecovered(i, rule.channelIndex);
        }
    }
}

} // namespace services
} // namespace datascope
