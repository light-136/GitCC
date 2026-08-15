/**
 * @file alarmengine.h
 * @brief V3 领域层 —— 报警引擎（纯逻辑类，无 QObject/无信号槽）
 *
 * ── 开发思路 ──
 * 报警引擎是"规则 + 状态"的评估器：
 *   - 规则集：addRule 收进若干条 AlarmRule；
 *   - 状态：每条规则对应一个 bool（当前是否处于报警态），用于消除重复触发。
 *
 * 每次来一个数据点，evaluate(dp) 只评估 channelIndex 匹配的规则，并且只在
 * "边界翻转"时产生事件：
 *   未触发 + isTriggered  → 触发：置报警态，产生 type=Trigger 事件；
 *   已触发 + isRecovered  → 恢复：清报警态，产生 type=Recover 事件。
 * 其余情况（还没触发、还在报警、还没恢复）都不产生事件 —— 这就是"边沿检测"，
 * 保证同一条规则不会每个采样帧都重复报警刷屏。
 *
 * 设计上的两个关键取舍：
 *   1. 纯逻辑类（非 QObject）：报警判定不需要事件循环、不需要跨线程信号，用
 *      "evaluate 直接返回 QVector<AlarmEvent>"这种同步调用即可，可被单元测试
 *      直接调用、也能放进任何采集线程跑，无元对象系统开销。这正是"领域层零
 *      QtWidgets/QtNetwork 依赖"的体现 —— 只依赖 QtCore（QVector/QDateTime）。
 *   2. 直接返回 QVector<AlarmEvent>：evaluate 可能一次性产生多条事件（多个规则
 *      同时越界），这是"多事件集合"而非"成功/失败二态"。按 V3 权威规格，不再
 *      包一层 Result 结构，也去掉事件 id（AlarmEvent 已精简为无 id 的 3 字段），
 *      因此引擎内部无需再维护自增计数器 m_nextEventId。
 *
 * ── WPF 对照 ──
 *   AlarmEngine.evaluate()  ↔  ViewModel 里"喂一个值 → 得到报警结论"的服务方法；
 *                             等价于 C# 方法返回 IReadOnlyList<AlarmEvent>。
 */

#pragma once

#include <QVector>

#include "domain/alarmrule.h"
#include "domain/datapoint.h"

namespace dscope {
namespace domain {

/**
 * @class AlarmEngine
 * @brief 报警规则评估引擎（纯逻辑，有状态：规则集 + 每规则当前报警态）
 *
 * 典型用法：
 *   AlarmEngine engine;
 *   engine.addRule(上限规则);
 *   // 每来一帧数据：
 *   QVector<AlarmEvent> events = engine.evaluate(dataPoint);
 *   for (const AlarmEvent &ev : events) { ... 上抛给 UI / 记录系统 ... }
 */
class AlarmEngine
{
public:
    /**
     * @brief 添加一条报警规则
     * @param rule 规则（值拷贝进内部规则集）
     * @return 规则在引擎内的索引（0 基，供 isRuleActive 查询用）
     * @note 同时追加一条 m_active=false（初始非报警态），保证规则与状态一一对应。
     */
    int addRule(const AlarmRule &rule);

    /**
     * @brief 清空全部规则（并清空对应报警态）
     */
    void clearRules();

    /**
     * @brief 当前规则数量
     */
    int ruleCount() const;

    /**
     * @brief 指定索引的规则当前是否处于报警态
     * @param ruleIndex 规则索引（越界返回 false，不崩溃）
     */
    bool isRuleActive(int ruleIndex) const;

    /**
     * @brief 评估一个数据点：对 channelIndex 匹配的所有规则做触发/恢复判定
     * @param dp 实时数据点（含通道号、工程值、主机到达时间戳）
     * @return 该点触发的所有事件（可能 0..N 条，事件 ts 取 dp.hostArrivalTs）
     * @note 这是有副作用的函数：会更新内部报警态（m_active）。
     */
    QVector<AlarmEvent> evaluate(const DataPoint &dp);

private:
    QVector<AlarmRule> m_rules;    ///< 报警规则集（与 m_active 下标一一对应）
    QVector<bool>      m_active;   ///< 每条规则当前是否处于报警态（去重/边沿检测）
};

} // namespace domain
} // namespace dscope
