/**
 * @file settingspage.h
 * @brief 设置页（P5 创建占位骨架，P7 填充配置界面）
 *
 * P5 设计意图：
 *   - 本页承载系统的全局配置入口（串口参数、采样率、界面主题等）；
 *   - P5 阶段只搭骨架，占位说明标注 P7 的接入点：基于 ConfigManager
 *     （INI 全局配置存储）的配置 UI；
 *   - setObjectName("settingsPage") 供全局 QSS 选择器定位背景/边框。
 *
 * 对应 WPF 的锚点：
 *   - 类似 WPF 的设置窗口（Settings/Options），通常用表单控件
 *     （TextBox/ComboBox/CheckBox）绑定配置项，保存时写回配置存储。
 */

#pragma once

#include <QWidget>

namespace datascope {
namespace ui {

/**
 * @class SettingsPage
 * @brief 设置页签（P7 将接入基于 ConfigManager 的配置界面）
 */
class SettingsPage : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造设置页
     * @param parent 父控件（主窗口的 QTabWidget 会以本页为页签容器）
     */
    explicit SettingsPage(QWidget *parent = nullptr);

    // 禁止拷贝：QObject 派生类不可复制
    Q_DISABLE_COPY(SettingsPage)

private:
    /**
     * @brief 构建本页 UI（顶部标题 + 居中占位说明）
     * @note 布局、父子层级、objectName 都在这里集中构建，方便阅读
     */
    void buildUi();
};

} // namespace ui
} // namespace datascope
