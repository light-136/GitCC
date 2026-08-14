/**
 * @file devicepage.cpp
 * @brief 设备管理页实现（P8/P9 将填充设备列表）
 *
 * P5 与 P8/P9 的分工（教学脉络）：
 *   - P5（本阶段）：搭建页签骨架，保证主窗口可切换、QSS 可定位；
 *   - P8（后续）：接入串口/网络设备枚举列表（QTableView + 设备发现服务），
 *     展示设备名称、通道数、状态等元数据；
 *   - P9（后续）：接入设备连接、断开与通道参数配置，并向下游数据链路
 *     （P12 采集线程）提供设备句柄。
 */

#include "ui/pages/devicepage.h"

#include <QVBoxLayout>
#include <QLabel>

namespace datascope {
namespace ui {

DevicePage::DevicePage(QWidget *parent)
    : QWidget(parent)
{
    // 页签统一命名：供全局 QSS 以 #devicePage 选择器定位背景/边框
    setObjectName(QStringLiteral("devicePage"));
    buildUi();
}

void DevicePage::buildUi()
{
    // 纵向布局：标题在上，占位说明居中占据剩余空间
    auto *layout = new QVBoxLayout(this);

    // ---- 顶部标题：标识本页功能（QSS 可通过 #pageTitle 定位字号/颜色）----
    auto *titleLabel = new QLabel(tr("设备管理"), this);
    titleLabel->setObjectName(QStringLiteral("pageTitle"));
    titleLabel->setAlignment(Qt::AlignCenter);

    // ---- 居中占位说明：写清"将来接什么、对应哪一阶段"，让阅读者一目了然 ----
    auto *placeholder = new QLabel(
        tr("本页将在 P8 接入设备发现与枚举列表（串口/网络设备、通道数、状态），\n"
           "并在 P9 接入设备连接、断开与通道参数配置。"),
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
