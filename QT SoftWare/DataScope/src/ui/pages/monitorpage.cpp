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
#include "ui/widgets/gaugewidget.h"
#include "ui/widgets/ledindicator.h"
#include "ui/widgets/linechartwidget.h"

#include <QDateTime>
#include <QFrame>
#include <QHBoxLayout>
#include <QLabel>
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
    buildUi();

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
    }
}

} // namespace ui
} // namespace datascope
