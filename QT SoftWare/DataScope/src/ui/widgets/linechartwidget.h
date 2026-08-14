/**
 * @file linechartwidget.h
 * @brief 实时滚动曲线控件（P14 自绘控件之一）
 *
 * P14 教学点（对照 WPF）：
 *   - 本控件用 QWidget + paintEvent 自绘，等价于 WPF 中继承 FrameworkElement
 *     并重写 OnRender(DrawingContext) 的自定义控件；
 *   - paintEvent 里的 QPainter ≈ DrawingContext，QPen/QBrush ≈ Pen/Brush，
 *     QPainterPath ≈ StreamGeometry + GeometryDrawing；
 *   - 控件自身背景透明，整体观感交给主 Agent 的 QSS 深色主题
 *     （#1e2a38 底、#16a085 主色）协调。
 */

#pragma once

#include <QWidget>
#include <QString>
#include <QVector>

namespace datascope {
namespace ui {

/**
 * @class LineChartWidget
 * @brief 实时滚动曲线（网格线 + 折线 + 标题，数据缓冲自动滚动淘汰）
 *
 * 数据缓冲设计：
 *   - m_data 头部是最旧点、尾部是最新点，appendPoint 追加到尾；
 *   - 超过 m_maxPoints 时 removeFirst 淘汰最旧点，形成"滚动"效果。
 */
class LineChartWidget : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造实时曲线控件
     * @param parent 父控件（通常放 QVBoxLayout 里）
     */
    explicit LineChartWidget(QWidget *parent = nullptr);

    /**
     * @brief 追加一个数据点到缓冲尾部，超出上限则淘汰最旧点（滚动）
     * @param value 待追加的数值
     */
    void appendPoint(double value);

    /**
     * @brief 设置 Y 轴量程（仅当 min < max 时生效，非法输入被忽略）
     * @param min 量程下限
     * @param max 量程上限
     */
    void setRange(double min, double max);

    /**
     * @brief 设置曲线标题（绘制在左上角）
     * @param title 标题文本
     */
    void setTitle(const QString &title);

    /**
     * @brief 清空数据缓冲
     */
    void clearData();

    /**
     * @brief 当前数据点数
     * @return m_data 的元素个数
     */
    int pointCount() const;

    /**
     * @brief 最后一个数据值
     * @return 缓冲为空时返回 0，否则返回最新追加的值
     */
    double lastValue() const;

    /**
     * @brief 缓冲上限
     * @return m_maxPoints
     */
    int maxPoints() const;

    // 以下两个只读访问器为单测"读回量程"而增加（接口契约外的扩展，不影响契约内方法）。
    /**
     * @brief 当前量程下限
     */
    double minimum() const;

    /**
     * @brief 当前量程上限
     */
    double maximum() const;

protected:
    /**
     * @brief 自绘入口：网格线 + 折线 + 标题
     * @param event 绘制事件（本实现忽略）
     */
    void paintEvent(QPaintEvent *event) override;

    /**
     * @brief 建议尺寸 400x200
     */
    QSize sizeHint() const override;

    /**
     * @brief 最小尺寸 200x100
     */
    QSize minimumSizeHint() const override;

private:
    QString m_title;        ///< 曲线标题（左上角绘制）
    double m_min = 0.0;     ///< Y 轴量程下限
    double m_max = 100.0;   ///< Y 轴量程上限
    QVector<double> m_data; ///< 数据缓冲（头部最旧、尾部最新）
    int m_maxPoints = 500;  ///< 缓冲上限（默认 500 点）
};

} // namespace ui
} // namespace datascope
