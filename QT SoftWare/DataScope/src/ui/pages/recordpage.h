/**
 * @file recordpage.h
 * @brief 数据记录与回放页（P5 创建占位骨架，P15 填充记录/回放功能）
 *
 * P5 设计意图：
 *   - 本页承载采集数据的"落盘"与"回放"两大职责，是数据价值的沉淀出口；
 *   - P5 阶段只搭骨架，占位说明标注 P15 的接入点：
 *       记录文件的创建与管理、时间轴回放、波形导出；
 *   - setObjectName("recordPage") 供全局 QSS 选择器定位背景/边框。
 *
 * 对应 WPF 的锚点：
 *   - 类似 WPF 的 DataGrid + 播放控件组合；回放走"时间轴 + 波形重绘"，
 *     与实时监控（P14）共用同一套波形绘制组件。
 */

#pragma once

#include <QWidget>

namespace datascope {
namespace ui {

/**
 * @class RecordPage
 * @brief 数据记录与回放页签（P15 将接入记录文件管理与时间轴回放）
 */
class RecordPage : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造数据记录与回放页
     * @param parent 父控件（主窗口的 QTabWidget 会以本页为页签容器）
     */
    explicit RecordPage(QWidget *parent = nullptr);

    // 禁止拷贝：QObject 派生类不可复制
    Q_DISABLE_COPY(RecordPage)

private:
    /**
     * @brief 构建本页 UI（顶部标题 + 居中占位说明）
     * @note 布局、父子层级、objectName 都在这里集中构建，方便阅读
     */
    void buildUi();
};

} // namespace ui
} // namespace datascope
