/**
 * @file monitorpage.h
 * @brief 监控总览页（P5 创建占位骨架，P14 填充实时曲线仪表）
 *
 * P5 设计意图：
 *   - 本页是主窗口 5 个页签之一（数据采集系统的"仪表盘"入口）；
 *   - P5 阶段只搭"骨架"：顶部标题 + 居中占位说明，让主 Agent 的全局 QSS
 *     有稳定的定位节点，也让后续 P14 有明确的替换点；
 *   - setObjectName("monitorPage") 供全局 QSS 选择器定位背景/边框；
 *   - P14 将把居中的占位说明替换为多通道实时曲线仪表（波形绘制 + 数据刷新）。
 *
 * 对应 WPF 的锚点：
 *   - 类似 WPF 中 TabControl 的一个 TabItem，宿主是主窗口的 QTabWidget；
 *   - objectName 对应 WPF 里的 x:Name，是样式（Style/Template）定位控件的钥匙。
 */

#pragma once

#include <QWidget>

namespace datascope {
namespace ui {

/**
 * @class MonitorPage
 * @brief 监控总览页签（P14 将接入多通道实时曲线仪表）
 */
class MonitorPage : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造监控总览页
     * @param parent 父控件（主窗口的 QTabWidget 会以本页为页签容器）
     */
    explicit MonitorPage(QWidget *parent = nullptr);

    // 禁止拷贝：QObject 派生类不可复制（对应 C# sealed + 无深拷贝语义）
    Q_DISABLE_COPY(MonitorPage)

private:
    /**
     * @brief 构建本页 UI（顶部标题 + 居中占位说明）
     * @note 布局、父子层级、objectName 都在这里集中构建，方便阅读
     */
    void buildUi();
};

} // namespace ui
} // namespace datascope
