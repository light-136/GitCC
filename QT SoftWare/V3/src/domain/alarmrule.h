/**
 * @file alarmrule.h
 * @brief V3 领域层 —— 报警规则与报警事件（header-only，无 .cpp）
 *
 * ── 开发思路 ──
 * 报警判定的"规则"与"结果"是两个不同层面的概念，这里拆成两个类型：
 *
 *   AlarmRule  —— 规则本身（值类型，无状态）：监控哪个通道、阈值、回差。它只回答
 *                 "给定一个值，是否触发 / 是否恢复"两个纯函数问题，本身不记录
 *                 "当前是否在报警"（那由 AlarmEngine 持有）。
 *   AlarmEvent —— 报警结果（值类型，一次边界事件）：规则真正越过触发/恢复边界时
 *                 产生的"事件记录"，带类型、通道号、时间戳。
 *
 * 为什么要有回差（hysteresis）：真实信号总在阈值附近抖动。假设上限 100、无回差，
 * 值在 99.9↔100.1 之间抖动时会反复触发/恢复，刷屏报警中心。加回差后：
 *   - 触发需 value > threshold；
 *   - 恢复需 value < threshold - hysteresis。
 * 即"触发"与"恢复"之间留出一个死区，抑制抖动 —— 这是工业报警的标准做法。
 *
 * 按 V3 权威规格：V3 只保留"超上限"一种规则（越下限不在第一阶段范围），因此
 * AlarmRule 不再携带比较方向枚举，触发/恢复语义直接写死在两个成员函数里。
 *
 * ── WPF 对照 ──
 *   struct AlarmRule   ↔  C# record / POCO（纯数据 + 少量纯函数）
 *   struct AlarmEvent  ↔  C# 的 DTO / 事件对象
 *   AlarmEvent::Type   ↔  C# 中嵌套在类型内的普通枚举（Type.Trigger / Type.Recover）
 */

#pragma once

#include <QDateTime>

namespace dscope {
namespace domain {

/**
 * @struct AlarmRule
 * @brief 一条"超上限 + 回差"报警规则（值类型，无状态）
 */
struct AlarmRule
{
    int    channelIndex = 0;      ///< 被监控的通道序号（0 基）
    double threshold    = 0.0;    ///< 报警阈值（工程值，越上限）
    double hysteresis   = 0.0;    ///< 回差（死区宽度，抑制抖动，见文件头说明）

    /**
     * @brief 给定工程值，是否越过触发边界
     * @param value 工程值
     * @return value > threshold
     * @note 纯函数，不修改自身。
     */
    bool isTriggered(double value) const { return value > threshold; }

    /**
     * @brief 给定工程值，是否越过恢复边界（已报警态下回到死区外）
     * @param value 工程值
     * @return value < threshold - hysteresis
     * @note 纯函数，不修改自身。
     */
    bool isRecovered(double value) const { return value < threshold - hysteresis; }
};

/**
 * @struct AlarmEvent
 * @brief 一次报警事件（触发或恢复，值类型）
 */
struct AlarmEvent
{
    /** @brief 事件类型（嵌套普通枚举：Trigger 触发 / Recover 恢复） */
    enum Type {
        Trigger,   ///< 触发（从未报警 → 报警）
        Recover    ///< 恢复（从报警 → 正常）
    };

    Type      type         = Trigger;   ///< 事件类型：触发 / 恢复
    int       channelIndex = 0;         ///< 所属通道序号（0 基）
    QDateTime ts;                       ///< 事件时间戳（主机到达时刻）
};

} // namespace domain
} // namespace dscope
