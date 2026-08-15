/**
 * @file monitorpage.h
 * @brief V3 UI 层 —— 监控页（纯渲染 + 编排，无 Presenter）
 *
 * ── 开发思路 ──
 * 监控页的数据流是单向 push（数据源 → 槽 → 控件 setValue/setAlarm/appendEvent），没有
 * "用户输入 → 领域"的反向流，也没有跨控件联动可单测的交互逻辑，因此按 UI 架构审查
 * "攻击点 1"的判据：纯渲染页直接用 Model/View + 信号槽直连，不套 Presenter。
 *
 * 报警可视化的"阈值到 UI"契约（审查"攻击点 4"的 P0）：
 *   - 阈值在 onChannelConfigReceived 里一次性注入 chart->setThreshold 与 AlarmEngine.addRule，
 *     绝不每帧查询、绝不订阅；
 *   - LED 状态由 AlarmEngine 的输出（evaluate 产生的事件 + isRuleActive）驱动，
 *     绝不在 UI 里另算一遍 `value > threshold`——彻底杜绝"LED 红但事件没触发"的双实现
 *     不一致（V1/V2 的 0.85 三进宫）。
 *
 * 布局：上方曲线（LineChartWidget） + 下方一行通道卡片（GaugeWidget + LedIndicator）+
 *       右侧报警中心（QListView + AlarmEventModel）。
 */

#pragma once

#include <QWidget>
#include <QVector>

#include "domain/alarmengine.h"     // dscope::domain::AlarmEngine / AlarmRule
#include "domain/channelconfig.h"   // dscope::domain::ChannelConfig
#include "domain/datapoint.h"       // dscope::domain::DataPoint

class QLabel;
class QListView;
class QHBoxLayout;

namespace dscope {
namespace ui {

class LineChartWidget;
class GaugeWidget;
class LedIndicator;
class AlarmEventModel;

/**
 * @class MonitorPage
 * @brief 实时监控页：曲线 + 通道仪表卡片 + 报警中心
 */
class MonitorPage : public QWidget
{
    Q_OBJECT

public:
    explicit MonitorPage(QWidget *parent = nullptr);

public slots:
    /** @brief 实时数据点：直绘 setValue + AlarmEngine.evaluate → LED + 报警列表 */
    void onPointsReady(const QVector<dscope::domain::DataPoint> &points);

    /** @brief 通道配置：重建通道卡片/仪表 + 一次性注入阈值 + 重建报警规则 */
    void onChannelConfigReceived(const QVector<dscope::domain::ChannelConfig> &channels);

    /** @brief 连接错误：状态提示 */
    void onConnectionError(int code);

    /** @brief 连接状态变化：状态提示 */
    void onConnectionStateChanged(int state);

    /** @brief 连接建立：状态提示 + 清空曲线与报警态 */
    void onConnected();

    /** @brief 连接断开：状态提示 */
    void onDisconnected();

private:
    /** @brief 单通道卡片（容器 + 仪表 + LED，标题由仪表内部绘制） */
    struct ChannelCard {
        QWidget      *container = nullptr;   ///< 卡片容器（QFrame）
        GaugeWidget  *gauge     = nullptr;   ///< 仪表盘
        LedIndicator *led       = nullptr;   ///< 报警 LED
    };

    /** @brief 用默认 4 通道构建初始布局（配置到达前 UI 不空白） */
    void buildDefaultChannels();

    /** @brief 依据通道配置重建：清空旧卡片 + 注入阈值 + 重建报警规则 */
    void rebuildChannels(const QVector<dscope::domain::ChannelConfig> &channels);

    /** @brief 清空所有通道卡片（deleteLater，容器清空） */
    void clearChannels();

    /**
     * @brief 依据通道配置派生默认报警规则（阈值=量程上限，回差=量程 5%）
     * @note Phase 1 采集层不下发 AlarmRule，此处是"默认派生策略"（阈值单一来源），
     *       后续协议若扩展阈值字段，只需替换本方法。
     */
    QVector<dscope::domain::AlarmRule> buildDefaultRules(
            const QVector<dscope::domain::ChannelConfig> &channels) const;

    LineChartWidget *m_chart       = nullptr;   ///< 曲线控件
    QHBoxLayout     *m_cardsLayout = nullptr;   ///< 通道卡片行布局
    QVector<ChannelCard> m_cards;               ///< 通道卡片集合（与通道号一一对应）
    AlarmEventModel *m_alarmModel  = nullptr;   ///< 报警事件模型
    QListView       *m_alarmView   = nullptr;   ///< 报警列表视图
    QLabel          *m_statusHint  = nullptr;   ///< 连接状态提示条

    dscope::domain::AlarmEngine m_alarmEngine;  ///< 报警评估引擎（领域对象，UI 只编排不实现规则）
};

} // namespace ui
} // namespace dscope
