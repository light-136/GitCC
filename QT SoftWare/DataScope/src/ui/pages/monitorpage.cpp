/**
 * @file monitorpage.cpp
 * @brief 监控总览页实现（P14 将填充实时曲线仪表）
 *
 * P5 与 P14 的分工（教学脉络）：
 *   - P5（本阶段）：搭建页签骨架，保证主窗口可切换、QSS 可定位；
 *   - P14（后续）：把占位说明替换为多通道曲线绘制区 + 数据刷新定时器，
 *     绘制引擎与实时数据流（P12 生产者-消费者）在此汇合。
 */

#include "ui/pages/monitorpage.h"

#include <QVBoxLayout>
#include <QLabel>

namespace datascope {
namespace ui {

MonitorPage::MonitorPage(QWidget *parent)
    : QWidget(parent)
{
    // 页签统一命名：供全局 QSS 以 #monitorPage 选择器定位背景/边框
    setObjectName(QStringLiteral("monitorPage"));
    buildUi();
}

void MonitorPage::buildUi()
{
    // 纵向布局：标题在上，占位说明居中占据剩余空间
    auto *layout = new QVBoxLayout(this);

    // ---- 顶部标题：标识本页功能（QSS 可通过 #pageTitle 定位字号/颜色）----
    auto *titleLabel = new QLabel(tr("监控总览"), this);
    titleLabel->setObjectName(QStringLiteral("pageTitle"));
    titleLabel->setAlignment(Qt::AlignCenter);

    // ---- 居中占位说明：写清"将来接什么、对应哪一阶段"，让阅读者一目了然 ----
    auto *placeholder = new QLabel(
        tr("本页将在 P14 接入多通道实时曲线仪表：\n"
           "多通道波形绘制、数据刷新与异常闪烁提醒，作为采集数据的实时监控总览。\n"
           "数据来源将对接 P12 的生产者-消费者采集链路。"),
        this);
    placeholder->setObjectName(QStringLiteral("pagePlaceholder"));
    placeholder->setAlignment(Qt::AlignCenter);
    placeholder->setWordWrap(true);

    layout->addWidget(titleLabel);
    // 拉伸因子 1：让占位说明占据标题之外的剩余垂直空间，视觉上"居中"
    layout->addWidget(placeholder, 1);
}

} // namespace ui
} // namespace datascope
