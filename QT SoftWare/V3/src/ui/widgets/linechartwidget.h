/**
 * @file linechartwidget.h
 * @brief V3 UI 层 —— 自绘多通道滚动曲线控件
 *
 * ── 开发思路 ──
 * 实时值以 10~30Hz × N 通道持续到达，是最高频的渲染负载。本控件按权威规格第 10 节
 * "实时值直绘 setValue，不走 dataChanged"设计：数据只进 `setValue` 追加到内存缓冲，
 * `paintEvent` 直接折线绘制，绝不经过 Model。
 *
 * 性能要点（对照 UI 架构审查"攻击点 3"）：
 *   1. 环形缓冲用 `std::deque<double>`：`push_back`/`pop_front` 两端 O(1)，规避 V2 的
 *      `removeFirst()` O(n) 前移。
 *   2. 图层缓存：背景 + 网格 + 阈值虚线画进一张 QPixmap，只在尺寸变化 / 阈值变化 /
 *      clear / setChannelCount 时重建；每帧只重绘折线，不重画静态元素。
 *   3. 节流落点：每次 setValue 只 `update()`，Qt 会把同一事件循环内的多次 update()
 *      合并为一次 paintEvent，采集端 30Hz 节流是上游职责。
 *
 * 坐标约定：Y 量程固定 0~100；X 轴为固定窗口（m_capacity 格），最新点恒在右缘，
 * 数据增多时整体左移，形成"横向滚动"。
 *
 * ── WPF 对照 ──
 *   自绘 LineChartWidget  ↔  WPF 无绑定、自绘的 Canvas/Polyline；setValue 相当于
 *                            给自定义控件喂一个点并 InvalidateVisual()。
 */

#pragma once

#include <QWidget>
#include <QVector>
#include <QPixmap>
#include <deque>

namespace dscope {
namespace ui {

/**
 * @class LineChartWidget
 * @brief 多通道实时滚动曲线（自绘，QPainter 抗锯齿，量程 0~100）
 */
class LineChartWidget : public QWidget
{
    Q_OBJECT

public:
    explicit LineChartWidget(QWidget *parent = nullptr);

    /** @brief 设置通道数量（重分配每通道缓冲，失效静态图层缓存） */
    void setChannelCount(int n);

    /** @brief 直绘追加一个值：写入该通道缓冲并触发重绘（不走 dataChanged） */
    void setValue(int channelIndex, double value);

    /** @brief 一次性注入阈值虚线（配置变化回调调用，非每帧） */
    void setThreshold(int channelIndex, double threshold);

    /** @brief 清空全部通道缓冲与阈值线 */
    void clear();

protected:
    void paintEvent(QPaintEvent *event) override;
    void resizeEvent(QResizeEvent *event) override;

private:
    /** @brief 重建静态图层缓存（背景 + 网格 + 阈值虚线 + 图例） */
    void rebuildBackgroundCache();

    int m_channelCount = 0;                       ///< 通道数量
    int m_capacity     = 600;                     ///< 每通道窗口内最大采样点数
    QVector<std::deque<double>> m_data;           ///< 每通道环形缓冲（两端 O(1)）
    QVector<double> m_thresholds;                 ///< 每通道阈值（NaN = 未设置）
    QVector<bool>   m_hasThreshold;               ///< 每通道是否已设置阈值
    QPixmap         m_bgCache;                    ///< 静态图层缓存（背景/网格/阈值线/图例）
};

} // namespace ui
} // namespace dscope
