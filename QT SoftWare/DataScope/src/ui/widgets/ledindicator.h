/**
 * @file ledindicator.h
 * @brief LED 指示灯控件（P14 自绘控件之三）
 *
 * P14 教学点（对照 WPF）：
 *   - 最简单的自绘控件：paintEvent 里画同心圆 + 径向渐变，
 *     对应 WPF 的 Ellipse + RadialGradientBrush；
 *   - 亮/灭只改状态成员，重绘全权交给 paintEvent，体现"数据驱动绘制"
 *     （类似 WPF 的 INotifyPropertyChanged + 重绘）。
 */

#pragma once

#include <QWidget>
#include <QColor>

namespace datascope {
namespace ui {

/**
 * @class LedIndicator
 * @brief LED 指示灯（圆形，亮=发光色，灭=暗灰）
 *
 * 用途：
 *   - 监控页的通道/设备运行状态指示（连接正常、采集启动、告警等）；
 *   - 默认灯色为工程主色青色 #16a085，可用 setColor 换成告警红等。
 */
class LedIndicator : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造 LED 指示灯
     * @param parent 父控件
     */
    explicit LedIndicator(QWidget *parent = nullptr);

    /**
     * @brief 设置亮/灭状态
     * @param on true=亮（发光色），false=灭（暗灰）
     */
    void setState(bool on);

    /**
     * @brief 设置灯色（仅在亮态时生效于发光色）
     * @param color 灯色
     */
    void setColor(const QColor &color);

    /**
     * @brief 查询当前亮灭状态
     * @return true=亮，false=灭
     */
    bool isOn() const;

protected:
    /**
     * @brief 自绘入口：圆形 LED（灭=暗色，亮=发光渐变 + 高光）
     * @param event 绘制事件（本实现忽略）
     */
    void paintEvent(QPaintEvent *event) override;

    /**
     * @brief 建议尺寸 20x20
     */
    QSize sizeHint() const override;

    /**
     * @brief 最小尺寸 12x12
     */
    QSize minimumSizeHint() const override;

private:
    bool m_on = false;                          ///< 亮灭状态
    QColor m_color = QColor(0x16, 0xa0, 0x85);  ///< 灯色（默认工程青色）
};

} // namespace ui
} // namespace datascope
