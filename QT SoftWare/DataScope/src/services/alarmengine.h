/**
 * @file alarmengine.h
 * @brief V2 报警引擎（服务层 —— 把报警逻辑从 View 移到这里）
 *
 * ────────────────────────────────────────────────────────────
 * 为什么需要这个类（对应审查 P0-3：报警逻辑写在 View 层）
 * ────────────────────────────────────────────────────────────
 * V1 里"报警"是 monitorpage.cpp 里一行 value > m_max*0.85 的内联代码：
 *   - 无法被测试（没有独立逻辑单元）；
 *   - 无法被其他模块复用（报警中心、记录系统都用不上）；
 *   - 阈值硬编码在 UI 里（改报警值要改代码）。
 *
 * V2 把它提取成独立的报警引擎，职责单一：
 *   1. 持有报警规则集（AlarmRule 列表）；
 *   2. 输入实时数据点（DataPoint）逐条评估规则；
 *   3. 输出报警事件（AlarmEvent）——仅在"未触发 → 触发"边界产生新事件，
 *      避免同一规则每个数据帧都重复报警；
 *   4. 值恢复后通知"报警恢复"（isTriggered 由 true 变 false）。
 *
 * ── WPF 对照 ──
 *   AlarmEngine.evaluate()  ↔   ViewModel 里的报警判定属性/服务
 *   ruleTriggered 信号       ↔   PropertyChanged / 事件聚合器
 *   AlarmRule/AlarmEvent     ↔   领域实体（报警规则 / 报警记录）
 *
 * ── 设计要点 ──
 *   - 纯逻辑：只依赖 QtCore + V2 领域类型，不依赖 UI/Network —— 可独立单测；
 *   - 状态保持：内部记录每个规则"当前是否处于报警态"（m_ruleActive），
 *     用于消除重复触发，并支持"报警恢复"语义；
 *   - 信号驱动：规则触发/恢复通过信号上抛，由上层（报警中心 Model）消费。
 */

#pragma once

#include <QObject>
#include <QVector>

#include "domain/domainmodel_v2.h"

namespace datascope {
namespace services {

/**
 * @class AlarmEngine
 * @brief 报警规则评估引擎（服务层）
 *
 * 用法：
 *   AlarmEngine engine;
 *   engine.addRule(rule1);
 *   // 每来一帧数据：
 *   engine.evaluate(dataPoint);   // 内部自动判断触发/恢复并 emit 信号
 */
class AlarmEngine : public QObject
{
    Q_OBJECT

public:
    explicit AlarmEngine(QObject *parent = nullptr);

    /** @brief 添加一条报警规则（返回规则索引；重复添加同 channel 允许） */
    int addRule(const datascope::domain::v2::AlarmRule &rule);

    /** @brief 规则数量 */
    int ruleCount() const { return m_rules.size(); }

    /** @brief 清空全部规则（同时复位所有触发状态） */
    void clearRules();

    /**
     * @brief 评估一个数据点：对匹配该通道的所有规则做触发/恢复判定
     * @param dp 实时数据点（含通道号/工程值/质量）
     * @note 质量非 Good 的数据不参与报警判定（坏数据不报假警）
     */
    void evaluate(const datascope::domain::v2::DataPoint &dp);

    /** @brief 某规则当前是否处于报警态 */
    bool isRuleActive(int ruleIndex) const;

signals:
    /**
     * @brief 规则触发：产生一条新报警事件
     * @param event 新报警（已赋值时间戳/级别/消息）
     */
    void ruleTriggered(const datascope::domain::v2::AlarmEvent &event);

    /**
     * @brief 规则恢复：值回到正常范围
     * @param ruleIndex 规则索引（用于定位要清除/标记恢复的报警）
     * @param channelIndex 通道号
     */
    void ruleRecovered(int ruleIndex, int channelIndex);

private:
    QVector<datascope::domain::v2::AlarmRule> m_rules;  ///< 报警规则集
    QVector<bool> m_ruleActive;  ///< 每个规则当前是否处于报警态（去重）
    quint64 m_nextEventId = 1;   ///< 报警事件自增 id（保证唯一、单调）
};

} // namespace services
} // namespace datascope
