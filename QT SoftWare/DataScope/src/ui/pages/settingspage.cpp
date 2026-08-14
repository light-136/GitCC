/**
 * @file settingspage.cpp
 * @brief 设置页实现（P7 将填充配置界面）
 *
 * P5 与 P7 的分工（教学脉络）：
 *   - P5（本阶段）：搭建页签骨架，保证主窗口可切换、QSS 可定位；
 *   - P7（后续）：接入配置界面，控件直接读写 ConfigManager
 *     （线程安全、类型安全的 INI 键值存储），保存即落盘；
 *   - 典型的配置项：串口参数（端口/波特率）、采样率、界面主题、日志级别。
 */

#include "ui/pages/settingspage.h"

#include <QVBoxLayout>
#include <QLabel>

namespace datascope {
namespace ui {

SettingsPage::SettingsPage(QWidget *parent)
    : QWidget(parent)
{
    // 页签统一命名：供全局 QSS 以 #settingsPage 选择器定位背景/边框
    setObjectName(QStringLiteral("settingsPage"));
    buildUi();
}

void SettingsPage::buildUi()
{
    // 纵向布局：标题在上，占位说明居中占据剩余空间
    auto *layout = new QVBoxLayout(this);

    // ---- 顶部标题：标识本页功能（QSS 可通过 #pageTitle 定位字号/颜色）----
    auto *titleLabel = new QLabel(tr("设置"), this);
    titleLabel->setObjectName(QStringLiteral("pageTitle"));
    titleLabel->setAlignment(Qt::AlignCenter);

    // ---- 居中占位说明：写清"将来接什么、对应哪一阶段"，让阅读者一目了然 ----
    auto *placeholder = new QLabel(
        tr("本页将在 P7 接入配置界面：\n"
           "基于 ConfigManager（INI 全局配置）的串口参数、采样率、界面主题等设置项。"),
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
