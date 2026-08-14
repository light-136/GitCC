/**
 * @file devicepage.h
 * @brief 设备管理页（P5 创建占位骨架，P8/P9 填充设备列表）
 *
 * P5 设计意图：
 *   - 本页承载"采集硬件设备"的接入与管理，是系统与物理设备之间的桥头堡；
 *   - P5 阶段只搭骨架，占位说明标注了后续两个阶段的接入点：
 *       P8（设备发现/枚举）与 P9（设备连接与控制）；
 *   - setObjectName("devicePage") 供全局 QSS 选择器定位背景/边框。
 *
 * 对应 WPF 的锚点：
 *   - 类似 WPF 的 ListView/TabItem 组合，后续会用 QTableView/QListView
 *     呈现设备列表，数据来自设备管理服务层。
 */

#pragma once

#include <QWidget>

namespace datascope {
namespace ui {

/**
 * @class DevicePage
 * @brief 设备管理页签（P8/P9 将接入串口/网络设备列表与控制）
 */
class DevicePage : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造设备管理页
     * @param parent 父控件（主窗口的 QTabWidget 会以本页为页签容器）
     */
    explicit DevicePage(QWidget *parent = nullptr);

    // 禁止拷贝：QObject 派生类不可复制
    Q_DISABLE_COPY(DevicePage)

private:
    /**
     * @brief 构建本页 UI（顶部标题 + 居中占位说明）
     * @note 布局、父子层级、objectName 都在这里集中构建，方便阅读
     */
    void buildUi();
};

} // namespace ui
} // namespace datascope
