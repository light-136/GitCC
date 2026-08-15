/**
 * @file monitorpage.cpp
 * @brief 监控总览页实现（P14 完整版）
 *
 * P5 与 P14 的分工（教学脉络）：
 *   - P5：搭建页签骨架，保证主窗口可切换、QSS 可定位；
 *   - P14：把占位说明替换为 4 通道"LED + 数值 + 曲线 + 仪表"卡片布局，
 *     并用演示 QTimer（正弦波）驱动数据，让监控页立即"动起来"；
 *   - P12：真实采集链路（DataService 生产者-消费者）接入后，
 *     停用演示 QTimer，改由 DataService::dataUpdated → onDataUpdated 驱动。
 *
 * 教学点（对应 WPF）：
 *   - 布局组合：QVBoxLayout 总排 + 每通道 QHBoxLayout 卡片（左信息列 + 曲线 + 仪表），
 *     对应 WPF 的 StackPanel/DockPanel 组合；
 *   - 数据源可替换：演示源与真实源都汇聚到 onDataUpdated(QVector<DataPoint>)，
 *     监控页不关心数据从哪来（对应 WPF View 与 ViewModel 的松耦合）；
 *   - LED 报警逻辑：值超量程上限的 alarmRatio → 红色，否则绿色，
 *     体现"业务状态 → 控件状态"的驱动关系。
 */

#include "ui/pages/monitorpage.h"

#include "domain/models.h"
#include "domain/domainmodel_v2.h"      // V2：AlarmEvent/AlarmRule（报警中心接线）
#include "services/alarmengine.h"       // V2：报警引擎（规则评估）
#include "ui/models/alarmeventmodel.h"  // V2：报警事件列表模型
#include "ui/widgets/gaugewidget.h"
#include "ui/widgets/ledindicator.h"
#include "ui/widgets/linechartwidget.h"

#include <QAbstractItemView>
#include <QDateTime>
#include <QFrame>
#include <QHBoxLayout>
#include <QLabel>
#include <QListView>
#include <QPushButton>
#include <QTimer>
#include <QVBoxLayout>
#include <QtMath>   // qSin() 正弦（QElapsedTimer 时间基准）
#include <QtGlobal> // qBound 钳制

namespace datascope {
namespace ui {

namespace {
/** @brief 演示定时器周期（毫秒）。100ms = 10Hz，滚动曲线刷新观感平滑 */
constexpr int kDemoTickMs = 100;
/** @brief 演示正弦相位步进（弧度）。每 tick 增加固定步长，模拟时间流逝 */
constexpr double kPhaseStep = 0.15;
} // namespace

MonitorPage::MonitorPage(QWidget *parent)
    : QWidget(parent)
{
    // 页签统一命名：供全局 QSS 以 #monitorPage 选择器定位背景/边框
    setObjectName(QStringLiteral("monitorPage"));

    // ---- V2 报警中心：引擎/模型先创建（onDataUpdated 依赖它们）----
    // 报警引擎只做规则评估（无 UI），报警模型只做事件存储（无 UI），
    // 二者与 View 分离 → 可独立单测（tst_alarmengine / tst_alarmeventmodel）。
    m_alarmEngine = new datascope::services::AlarmEngine(this);
    m_alarmModel  = new datascope::ui::models::AlarmEventModel(this);
    setupAlarmRules();   // 依据通道量程派生报警规则（阈值来自通道配置）

    buildUi();           // 标题 + 4 通道卡片 + 底部报警中心

    // ---- 报警引擎 → 报警中心接线（数据流闭环）----
    // 触发：新报警事件 → 列表头插（最新在最上方）
    connect(m_alarmEngine, &datascope::services::AlarmEngine::ruleTriggered,
            this, [this](const datascope::domain::v2::AlarmEvent &event) {
                m_alarmModel->appendEvent(event);
            });
    // 恢复：值回到正常范围 → 记一条"报警恢复"（Info 级），与报警同列可追溯
    connect(m_alarmEngine, &datascope::services::AlarmEngine::ruleRecovered,
            this, [this](int ruleIndex, int channelIndex) {
                Q_UNUSED(ruleIndex);
                datascope::domain::v2::AlarmEvent ev(
                    m_recoverId++, -1, channelIndex,
                    datascope::domain::v2::AlarmSeverity::Info,
                    QStringLiteral("通道%1 报警恢复").arg(channelIndex + 1),
                    QDateTime::currentDateTime());
                m_alarmModel->appendEvent(ev);
            });

    // ---- 演示数据源：QTimer 定时回调，100ms 产生一组数据 ----
    // 教学点：QTimer + connect(&QTimer::timeout, lambda) 是 Qt 最常见的
    // "周期性工作"写法，对应 WPF 的 DispatcherTimer。
    m_demoTimer = new QTimer(this);
    m_demoTimer->setInterval(kDemoTickMs);
    connect(m_demoTimer, &QTimer::timeout, this, [this] { onDemoTick(); });
}

void MonitorPage::buildUi()
{
    // 纵向布局：标题在上，4 张通道卡片依次排列
    auto *layout = new QVBoxLayout(this);
    layout->setSpacing(8);
    layout->setContentsMargins(8, 8, 8, 8);

    // ---- 顶部标题：标识本页功能（QSS 通过 #pageTitle 定位字号/颜色）----
    auto *titleLabel = new QLabel(tr("监控总览"), this);
    titleLabel->setObjectName(QStringLiteral("pageTitle"));
    titleLabel->setAlignment(Qt::AlignCenter);

    layout->addWidget(titleLabel);

    // ---- 4 张通道卡片 ----
    for (int i = 0; i < kChannelCount; ++i) {
        layout->addWidget(buildChannelCard(i), /*stretch=*/1);
    }

    // ---- V2 底部报警中心（固定高度，不挤压上方卡片）----
    layout->addWidget(buildAlarmCenter());
}

QWidget *MonitorPage::buildAlarmCenter()
{
    // 报警中心面板：标题 + 报警列表 + "确认全部报警"按钮
    auto *panel = new QFrame(this);
    panel->setObjectName(QStringLiteral("alarmPanel"));  // QSS 定位报警区边框

    auto *col = new QVBoxLayout(panel);
    col->setContentsMargins(8, 4, 8, 4);
    col->setSpacing(4);

    // 标题行：标题 + 清除按钮（右对齐）
    auto *titleRow = new QHBoxLayout;
    auto *titleLabel = new QLabel(tr("报警中心"), panel);
    titleLabel->setObjectName(QStringLiteral("alarmTitle"));
    titleRow->addWidget(titleLabel);
    titleRow->addStretch(1);

    auto *clearBtn = new QPushButton(tr("确认全部"), panel);
    clearBtn->setFixedWidth(96);
    titleRow->addWidget(clearBtn);
    col->addLayout(titleRow);

    // 报警列表（QListView + AlarmEventModel：最新报警在上方，滚动展示）
    m_alarmList = new QListView(panel);
    m_alarmList->setModel(m_alarmModel);
    m_alarmList->setSelectionMode(QAbstractItemView::NoSelection);  // 只读展示
    m_alarmList->setEditTriggers(QAbstractItemView::NoEditTriggers);
    m_alarmList->setFixedHeight(130);  // 固定高度：报警中心是辅助区，不抢监控主区
    col->addWidget(m_alarmList);

    // "确认全部" → 清空列表（报警记录不删除，确认后视觉收敛）
    connect(clearBtn, &QPushButton::clicked, m_alarmModel, &datascope::ui::models::AlarmEventModel::clear);

    return panel;
}

void MonitorPage::setupAlarmRules()
{
    // 依据当前通道量程派生报警规则：每通道"超过 量程上限 × 报警比例"触发
    // 阈值来自通道配置（m_max × m_alarmRatio），描述可读（"通道N 值超限"）。
    // 后续通道配置改为模型驱动后，本函数改为从真实 ChannelConfig 派生。
    for (int i = 0; i < kChannelCount; ++i) {
        datascope::domain::v2::AlarmRule rule;
        rule.channelIndex = i;
        rule.condition    = datascope::domain::v2::AlarmCondition::AboveHigh;
        rule.threshold    = m_max[i] * m_alarmRatio[i];
        rule.description  = QStringLiteral("通道%1 值超限").arg(i + 1);
        m_alarmEngine->addRule(rule);
    }
}

QWidget *MonitorPage::buildChannelCard(int index)
{
    // 每通道一张卡片（QFrame 视觉分组），横向排布：
    // [信息列：名称+LED+当前值] | [实时曲线（弹性拉伸）] | [仪表盘]
    auto *card = new QFrame(this);
    card->setObjectName(QStringLiteral("channelCard")); // QSS 定位卡片边框

    auto *row = new QHBoxLayout(card);
    row->setContentsMargins(8, 6, 8, 6);
    row->setSpacing(10);

    // ---- 信息列：通道名 + LED + 当前值文本 ----
    const char *names[] = { "温度", "压力", "流量", "振动" };
    const char *units[] = { "℃", "MPa", "m³/h", "mm/s" };

    auto *infoCol = new QVBoxLayout;
    infoCol->setSpacing(4);

    // 通道名（QSS 可通过 #channelName 定位）
    auto *nameLabel = new QLabel(QString::fromUtf8(names[index]), card);
    nameLabel->setObjectName(QStringLiteral("channelName"));

    // LED 状态灯：默认绿（正常），值超限变红
    auto *led = new LedIndicator(card);
    led->setState(true); // 演示源默认"运行中=正常亮"

    // 当前值标签（"50.0 ℃"），QSS 通过 #channelValue 定位
    auto *valueLabel = new QLabel(QStringLiteral("--"), card);
    valueLabel->setObjectName(QStringLiteral("channelValue"));
    valueLabel->setAlignment(Qt::AlignHCenter);

    infoCol->addWidget(nameLabel);
    infoCol->addWidget(led, 0, Qt::AlignHCenter);
    infoCol->addWidget(valueLabel);
    infoCol->addStretch(1);

    // ---- 实时曲线（占据卡片剩余宽度，弹性拉伸）----
    auto *chart = new LineChartWidget(card);
    chart->setTitle(QString::fromUtf8(names[index]));
    chart->setRange(0.0, m_max[index]); // 量程 0..max

    // ---- 仪表盘（固定宽度）----
    auto *gauge = new GaugeWidget(card);
    gauge->setRange(0.0, m_max[index]);
    gauge->setUnit(QString::fromUtf8(units[index]));
    gauge->setFixedWidth(160);

    row->addLayout(infoCol);
    row->addWidget(chart, /*stretch=*/1);
    row->addWidget(gauge);

    // ---- 记录控件指针，供 onDataUpdated 按通道驱动 ----
    m_channels[index].chart = chart;
    m_channels[index].gauge = gauge;
    m_channels[index].led   = led;
    m_channels[index].valueLabel = valueLabel;

    return card;
}

bool MonitorPage::isDemoRunning() const
{
    return m_demoRunning;
}

void MonitorPage::setDemoRunning(bool enabled)
{
    m_demoRunning = enabled;
    if (enabled) {
        m_demoTimer->start(); // 启动演示：开始定时产生正弦数据
    } else {
        m_demoTimer->stop();
    }
}

void MonitorPage::onDemoTick()
{
    // 演示数据源：随时间推进产生 4 路正弦波，打包成 DataPoint 走统一入口。
    // 教学点：这里演示"生产者"，onDataUpdated 是"消费者"——两者通过
    // QVector<DataPoint> 解耦。P12 真实链路里，生产者换成采集线程的
    // DataService，消费者仍是一个 onDataUpdated。
    m_phase += kPhaseStep;

    QVector<domain::DataPoint> points;
    points.reserve(kChannelCount);

    for (int i = 0; i < kChannelCount; ++i) {
        domain::DataPoint dp;
        dp.channelIndex = i;
        // 正弦波：value = center + amp * sin(phase * freq)
        dp.value = m_center[i] + m_amplitude[i] * qSin(m_phase * m_frequency[i]);
        dp.timestamp = QDateTime::currentDateTime();
        points.append(dp);
    }

    onDataUpdated(points);
}

void MonitorPage::onDataUpdated(const QVector<domain::DataPoint> &points)
{
    // ---- 消费一组采样点：逐点找到对应通道控件组并驱动 ----
    // 教学点：QVector<DataPoint> 是"批量投递"的载体——比逐点信号槽调用
    // 更高效（跨线程队列投递一次到达），对应 WPF 里集合批量刷新。
    for (const domain::DataPoint &dp : points) {
        if (dp.channelIndex < 0 || dp.channelIndex >= kChannelCount) {
            continue; // 防御：越界通道号直接跳过（脏数据进不来）
        }
        ChannelUi &ui = m_channels[dp.channelIndex];

        // 1) 曲线追加一个点（内部自动滚动淘汰）
        ui.chart->appendPoint(dp.value);

        // 2) 仪表指针指向当前值（越界自动钳制、NaN 保护在控件内）
        ui.gauge->setValue(dp.value);

        // 3) 数值文本：一位小数（如 "50.0"），超量程时追加 "!" 提示
        if (ui.valueLabel) {
            QString text = QStringLiteral("%1").arg(dp.value, 0, 'f', 1);
            if (dp.value > m_max[dp.channelIndex]) {
                text += QStringLiteral(" !"); // 超限标记（值越界但控件已钳制）
            }
            ui.valueLabel->setText(text);
        }

        // 4) LED 报警逻辑：值超量程上限 * alarmRatio → 红（异常），否则绿（正常）
        const double alarm = m_max[dp.channelIndex] * m_alarmRatio[dp.channelIndex];
        if (dp.value > alarm) {
            ui.led->setColor(QColor(0xe7, 0x4c, 0x3c)); // 报警红
        } else {
            ui.led->setColor(QColor(0x16, 0xa0, 0x85)); // 正常绿（工程主色）
        }

        // 5) V2 报警引擎：V1 数据点 → V2 DataPoint → 规则评估。
        //    触发/恢复由引擎内部去重判定，信号驱动报警中心更新。
        datascope::domain::v2::DataPoint v2dp;
        v2dp.channelIndex = dp.channelIndex;
        v2dp.rawValue     = dp.value;
        v2dp.engValue     = dp.value;   // 当前无工程换算：工程值 = 原始值
        v2dp.quality      = datascope::domain::v2::DataQuality::Good;
        v2dp.timestamp    = dp.timestamp;
        m_alarmEngine->evaluate(v2dp);
    }
}

} // namespace ui
} // namespace datascope
