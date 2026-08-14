/**
 * @file recordpage.cpp
 * @brief 数据记录与回放页实现（P15 将填充记录/回放功能）
 *
 * P5 与 P15 的分工（教学脉络）：
 *   - P5（本阶段）：搭建页签骨架，保证主窗口可切换、QSS 可定位；
 *   - P15（后续）：接入记录文件管理（开始/停止记录、文件列表）、
 *     时间轴回放（进度条 + 波形重绘）与波形导出；
 *   - 记录数据来源对接 P12 的采集线程，存储层可复用 P6 日志/文件基础设施。
 */

#include "ui/pages/recordpage.h"

#include <QVBoxLayout>
#include <QLabel>

namespace datascope {
namespace ui {

RecordPage::RecordPage(QWidget *parent)
    : QWidget(parent)
{
    // 页签统一命名：供全局 QSS 以 #recordPage 选择器定位背景/边框
    setObjectName(QStringLiteral("recordPage"));
    buildUi();
}

void RecordPage::buildUi()
{
    // 纵向布局：标题在上，占位说明居中占据剩余空间
    auto *layout = new QVBoxLayout(this);

    // ---- 顶部标题：标识本页功能（QSS 可通过 #pageTitle 定位字号/颜色）----
    auto *titleLabel = new QLabel(tr("数据记录与回放"), this);
    titleLabel->setObjectName(QStringLiteral("pageTitle"));
    titleLabel->setAlignment(Qt::AlignCenter);

    // ---- 居中占位说明：写清"将来接什么、对应哪一阶段"，让阅读者一目了然 ----
    auto *placeholder = new QLabel(
        tr("本页将在 P15 接入数据记录与回放：\n"
           "记录文件的创建与管理、时间轴回放、波形导出，回放数据来自 P12 采集链路落盘。"),
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
