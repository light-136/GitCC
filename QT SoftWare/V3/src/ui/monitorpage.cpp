/**
 * @file monitorpage.cpp
 * @brief V3 UI 层 —— 监控页实现
 *
 * ── 开发思路 ──
 * 实时值、报警事件、通道配置三条数据流在槽里分流（权威规格第 10 节）：
 *   onPointsReady         → 直绘 setValue（曲线/仪表）+ AlarmEngine.evaluate（边沿事件 → LED/列表）
 *   onChannelConfigReceived → 重建卡片 + 一次性注入阈值（chart->setThreshold + addRule）
 *   连接状态/错误           → 状态提示条
 * LED 的亮灭完全由 AlarmEngine 输出驱动（isRuleActive），UI 不再独立计算阈值。
 */

#include "ui/monitorpage.h"

#include "ui/models/alarmeventmodel.h"
#include "ui/theme.h"
#include "ui/widgets/gaugewidget.h"
#include "ui/widgets/ledindicator.h"
#include "ui/widgets/linechartwidget.h"

#include "domain/connectionstate.h"   // connectionStateName
#include "domain/errorcode.h"         // errorCodeName

#include <QFrame>
#include <QHBoxLayout>
#include <QLabel>
#include <QListView>
#include <QPalette>
#include <QVBoxLayout>

namespace dscope {
namespace ui {

namespace {

/** @brief 给普通 QWidget/QFrame 刷纯色背景（调色板方式，不写裸 hex/QSS） */
void applyBackground(QWidget *widget, const QColor &color)
{
    QPalette pal = widget->palette();
    pal.setColor(QPalette::Window, color);
    widget->setPalette(pal);
    widget->setAutoFillBackground(true);
}

/** @brief 给报警列表视图刷面板背景/文字色（行前景色由模型的 ForegroundRole 决定） */
void applyListViewPalette(QListView *view)
{
    QPalette pal = view->palette();
    pal.setColor(QPalette::Base, Theme::panelBackground());
    pal.setColor(QPalette::Text, Theme::textPrimary());
    view->setPalette(pal);
}

} // namespace

MonitorPage::MonitorPage(QWidget *parent)
    : QWidget(parent)
{
    applyBackground(this, Theme::background());

    auto *root = new QVBoxLayout(this);
    root->setContentsMargins(8, 8, 8, 8);
    root->setSpacing(6);

    // 状态提示条
    m_statusHint = new QLabel(QStringLiteral("未连接"), this);
    m_statusHint->setFixedHeight(20);
    m_statusHint->setAlignment(Qt::AlignLeft | Qt::AlignVCenter);
    root->addWidget(m_statusHint);

    // 上方：曲线
    m_chart = new LineChartWidget(this);
    m_chart->setMinimumHeight(220);
    root->addWidget(m_chart, 3);

    // 下方：仪表区 + 报警列表
    auto *bottom = new QHBoxLayout();
    bottom->setSpacing(6);
    root->addLayout(bottom, 2);

    // 通道卡片行
    m_cardsLayout = new QHBoxLayout();
    m_cardsLayout->setSpacing(6);
    bottom->addLayout(m_cardsLayout, 1);

    // 报警面板（右侧）
    auto *alarmWrap = new QFrame(this);
    applyBackground(alarmWrap, Theme::panelBackground());
    alarmWrap->setFixedWidth(300);
    auto *alarmLayout = new QVBoxLayout(alarmWrap);
    alarmLayout->setContentsMargins(6, 6, 6, 6);
    alarmLayout->setSpacing(4);

    auto *alarmTitle = new QLabel(QStringLiteral("报警中心"), alarmWrap);
    alarmTitle->setFont(Theme::titleFont());
    alarmTitle->setFixedHeight(22);
    QPalette titlePal = alarmTitle->palette();
    titlePal.setColor(QPalette::WindowText, Theme::textPrimary());
    alarmTitle->setPalette(titlePal);
    alarmLayout->addWidget(alarmTitle);

    m_alarmModel = new AlarmEventModel(this);
    m_alarmView = new QListView(alarmWrap);
    m_alarmView->setModel(m_alarmModel);
    m_alarmView->setEditTriggers(QAbstractItemView::NoEditTriggers);
    m_alarmView->setSelectionMode(QAbstractItemView::NoSelection);
    m_alarmView->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    applyListViewPalette(m_alarmView);
    alarmLayout->addWidget(m_alarmView, 1);

    bottom->addWidget(alarmWrap);

    // 默认 4 通道布局（配置到达前 UI 不空白）
    buildDefaultChannels();
}

void MonitorPage::onPointsReady(const QVector<dscope::domain::DataPoint> &points)
{
    for (const dscope::domain::DataPoint &dp : points) {
        // 1) 直绘曲线（实时值不走 dataChanged）
        m_chart->setValue(dp.channelIndex, dp.value);

        // 2) 直绘仪表（有对应卡片才更新）
        if (dp.channelIndex >= 0 && dp.channelIndex < m_cards.size())
            m_cards.at(dp.channelIndex).gauge->setValue(dp.value);

        // 3) 报警评估（领域对象，UI 只编排、不实现规则）
        const QVector<dscope::domain::AlarmEvent> events = m_alarmEngine.evaluate(dp);
        for (const dscope::domain::AlarmEvent &ev : events) {
            m_alarmModel->appendEvent(ev);

            // LED 状态取自 AlarmEngine（isRuleActive），绝不独立计算阈值——
            // 这保证 LED 与报警事件永远同源一致（V1/V2 "0.85 双实现"的根治）。
            if (ev.channelIndex >= 0 && ev.channelIndex < m_cards.size())
                m_cards.at(ev.channelIndex).led->setAlarm(m_alarmEngine.isRuleActive(ev.channelIndex));
        }
    }
}

void MonitorPage::onChannelConfigReceived(const QVector<dscope::domain::ChannelConfig> &channels)
{
    if (channels.isEmpty())
        return;

    rebuildChannels(channels);
    m_statusHint->setText(QStringLiteral("通道配置已更新：%1 通道").arg(channels.size()));
}

void MonitorPage::onConnectionError(int code)
{
    const auto error = static_cast<dscope::domain::ErrorCode>(code);
    m_statusHint->setText(QStringLiteral("连接错误：%1").arg(dscope::domain::errorCodeName(error)));
}

void MonitorPage::onConnectionStateChanged(int state)
{
    const auto st = static_cast<dscope::domain::ConnectionState>(state);
    m_statusHint->setText(dscope::domain::connectionStateName(st));
}

void MonitorPage::onConnected()
{
    m_statusHint->setText(QStringLiteral("已连接"));
    m_chart->clear();                       // 新会话，清空旧曲线
    for (const ChannelCard &card : m_cards)
        card.led->setAlarm(false);          // LED 复位（规则会在配置到达时重建）
}

void MonitorPage::onDisconnected()
{
    m_statusHint->setText(QStringLiteral("未连接"));
}

void MonitorPage::buildDefaultChannels()
{
    // 默认 4 通道：名称"通道N"、无单位、量程 0~100
    QVector<dscope::domain::ChannelConfig> defaults;
    for (int i = 0; i < 4; ++i) {
        dscope::domain::ChannelConfig cfg;
        cfg.name     = QStringLiteral("通道%1").arg(i + 1);
        cfg.unit     = QString();
        cfg.rangeMin = 0.0;
        cfg.rangeMax = 100.0;
        defaults.append(cfg);
    }
    rebuildChannels(defaults);
}

void MonitorPage::rebuildChannels(const QVector<dscope::domain::ChannelConfig> &channels)
{
    clearChannels();

    m_chart->setChannelCount(channels.size());
    m_chart->clear();

    m_alarmEngine.clearRules();   // 规则随配置重建

    // 阈值单一来源：先一次性派生默认规则，再同时注入曲线阈值线 + 报警引擎
    const QVector<dscope::domain::AlarmRule> rules = buildDefaultRules(channels);

    for (int i = 0; i < channels.size(); ++i) {
        const dscope::domain::ChannelConfig &cfg = channels.at(i);

        // 通道卡片：LED（左上）+ 仪表（含通道名标题）
        ChannelCard card;
        card.container = new QFrame(this);
        applyBackground(card.container, Theme::panelBackground());
        auto *cardLayout = new QVBoxLayout(card.container);
        cardLayout->setContentsMargins(6, 4, 6, 6);
        cardLayout->setSpacing(2);

        card.led = new LedIndicator(card.container);
        card.led->setFixedSize(16, 16);
        card.led->setAlarm(false);
        cardLayout->addWidget(card.led, 0, Qt::AlignLeft);

        card.gauge = new GaugeWidget(card.container);
        card.gauge->setRange(cfg.rangeMin, cfg.rangeMax);
        card.gauge->setUnit(cfg.unit);
        card.gauge->setTitle(cfg.name);
        cardLayout->addWidget(card.gauge, 1);

        m_cardsLayout->addWidget(card.container, 1);
        m_cards.append(card);

        // 一次性注入阈值虚线（配置变化回调，非每帧）
        if (i < rules.size())
            m_chart->setThreshold(i, rules.at(i).threshold);

        // 报警规则入引擎（规则索引 == 通道号：按通道顺序逐条 addRule）
        if (i < rules.size())
            m_alarmEngine.addRule(rules.at(i));
    }
}

void MonitorPage::clearChannels()
{
    for (const ChannelCard &card : m_cards) {
        if (card.container) {
            m_cardsLayout->removeWidget(card.container);
            card.container->deleteLater();   // 延迟销毁，避免在信号处理中直接 delete
        }
    }
    m_cards.clear();
}

QVector<dscope::domain::AlarmRule> MonitorPage::buildDefaultRules(
        const QVector<dscope::domain::ChannelConfig> &channels) const
{
    QVector<dscope::domain::AlarmRule> rules;
    rules.reserve(channels.size());

    for (int i = 0; i < channels.size(); ++i) {
        const dscope::domain::ChannelConfig &cfg = channels.at(i);

        dscope::domain::AlarmRule rule;
        rule.channelIndex = i;
        // 默认派生策略（Phase 1 无阈值下发通道）：上限阈值 = 量程上限，回差 = 量程 5%
        rule.threshold  = cfg.rangeMax;
        rule.hysteresis = (cfg.rangeMax - cfg.rangeMin) * 0.05;
        rules.append(rule);
    }
    return rules;
}

} // namespace ui
} // namespace dscope
