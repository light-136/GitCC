/**
 * @file gaugewidget.h
 * @brief 仪表盘控件（P14 自绘控件之二）
 *
 * P14 教学点（对照 WPF）：
 *   - 表盘的弧形刻度用 QPainter::drawArc（对应 WPF DrawingContext.DrawArc）；
 *   - 指针用 QPainterPath 三角形 + 角度→坐标换算（三角函数），
 *     对应 WPF 里在 OnRender 中用 StreamGeometry 构造指针；
 *   - 角度/坐标映射是本控件的核心算法，实现文件中写有详细中文注释。
 */

#pragma once

#include <QWidget>
#include <QString>

namespace datascope {
namespace ui {

/**
 * @class GaugeWidget
 * @brief 仪表盘（扇形刻度 + 指针 + 数值/单位），值越界自动钳制
 *
 * 状态约束：
 *   - m_value 始终被钳制在 [m_min, m_max] 内；
 *   - setValue 收到 NaN 时直接落到量程下限，防止状态被污染。
 */
class GaugeWidget : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造仪表盘
     * @param parent 父控件
     */
    explicit GaugeWidget(QWidget *parent = nullptr);

    /**
     * @brief 设置量程（仅当 min < max 时生效）
     * @param min 量程下限
     * @param max 量程上限
     */
    void setRange(double min, double max);

    /**
     * @brief 设置指针指向的值（越界钳制，NaN 保护）
     * @param value 新值
     */
    void setValue(double value);

    /**
     * @brief 设置单位文本（显示在表盘下方）
     * @param unit 单位，如 "MPa"
     */
    void setUnit(const QString &unit);

    /**
     * @brief 当前值（已被钳制到量程内）
     */
    double value() const;

    /**
     * @brief 量程下限
     */
    double minimum() const;

    /**
     * @brief 量程上限
     */
    double maximum() const;

protected:
    /**
     * @brief 自绘入口：扇形刻度 + 指针 + 数值/单位
     * @param event 绘制事件（本实现忽略）
     */
    void paintEvent(QPaintEvent *event) override;

    /**
     * @brief 建议尺寸 200x200
     */
    QSize sizeHint() const override;

    /**
     * @brief 最小尺寸 120x120
     */
    QSize minimumSizeHint() const override;

private:
    double m_min = 0.0;      ///< 量程下限
    double m_max = 100.0;    ///< 量程上限
    double m_value = 0.0;    ///< 当前指针值（恒在 [m_min, m_max] 内）
    QString m_unit;          ///< 单位文本
};

} // namespace ui
} // namespace datascope
