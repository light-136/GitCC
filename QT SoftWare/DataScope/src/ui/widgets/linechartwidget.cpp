/**
 * @file linechartwidget.cpp
 * @brief 实时滚动曲线控件实现
 *
 * 绘制顺序（paintEvent）：
 *   1. 用 QFontMetrics 计算左侧留白，保证最宽的 Y 轴刻度标签完整显示；
 *   2. 绘制绘图区背景（半透明深色，与主 QSS 协调）；
 *   3. 绘制 Y 轴水平网格线与刻度标签（5 条水平线）；
 *   4. 按数据缓冲绘制青色折线（QPainterPath），空数据画提示、单点画圆点；
 *   5. 左上角绘制标题、右上角显示最新值。
 *
 * 坐标映射（教学重点）：
 *   - 值→像素（Y 方向线性映射）：顶部 = 量程最大值，底部 = 量程最小值；
 *   - 索引→像素（X 方向固定步进）：步进 = 绘图区宽 / (缓冲上限 - 1)，
 *     最新点锚定在右端，数据不满时曲线自右向左展开，满后表现为"滚动"。
 */

#include "ui/widgets/linechartwidget.h"

#include <QPainter>
#include <QPainterPath>
#include <QPen>
#include <QBrush>
#include <QFont>
#include <QFontMetrics>
#include <QRectF>
#include <QPointF>
#include <QColor>

namespace {
// 匿名命名空间：文件内私有工具函数（对应 C# 的 private static 方法）

// 把数据值线性映射为绘图区 Y 像素坐标（顶部为最大值、底部为最小值）
// 越界值会被钳制到绘图区上下沿，避免折线画出控件范围
qreal valueToY(double value, double min, double max, const QRectF &plot)
{
    const double range = max - min;
    const double safe = (range > 0.0) ? range : 1.0;   // 防御：避免除零
    double frac = (value - min) / safe;
    frac = qBound(0.0, frac, 1.0);                     // 钳制到 [0,1]
    return plot.bottom() - frac * plot.height();       // 值越大越靠上
}

// 数值格式化：整数不显示小数，非整数保留 1 位小数（刻度/最新值共用）
QString formatNumber(double v)
{
    if (qAbs(v - qRound(v)) < 1e-9)
        return QString::number(qRound(v));
    return QString::number(v, 'f', 1);
}
} // namespace

namespace datascope {
namespace ui {

LineChartWidget::LineChartWidget(QWidget *parent)
    : QWidget(parent)
{
    // 控件自身背景透明（默认），整体底色由父容器 QSS 决定
}

void LineChartWidget::appendPoint(double value)
{
    m_data.append(value);                  // 新点加入尾部（最新）
    while (m_data.size() > m_maxPoints)    // 超过缓冲上限时
        m_data.removeFirst();              // 丢弃最旧点（滚动淘汰）
    update();                              // 请求重绘
}

void LineChartWidget::setRange(double min, double max)
{
    if (min >= max)
        return;                            // 非法量程（min>=max）不生效
    m_min = min;
    m_max = max;
    update();
}

void LineChartWidget::setTitle(const QString &title)
{
    m_title = title;
    update();
}

void LineChartWidget::clearData()
{
    m_data.clear();
    update();
}

int LineChartWidget::pointCount() const
{
    return m_data.size();
}

double LineChartWidget::lastValue() const
{
    return m_data.isEmpty() ? 0.0 : m_data.constLast();
}

int LineChartWidget::maxPoints() const
{
    return m_maxPoints;
}

double LineChartWidget::minimum() const
{
    return m_min;
}

double LineChartWidget::maximum() const
{
    return m_max;
}

QSize LineChartWidget::sizeHint() const
{
    return QSize(400, 200);
}

QSize LineChartWidget::minimumSizeHint() const
{
    return QSize(200, 100);
}

void LineChartWidget::paintEvent(QPaintEvent *event)
{
    Q_UNUSED(event);
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing, true);   // 抗锯齿

    const QRectF rc = rect();                              // 控件整体区域

    // ---- 左侧留白：用字体度量保证最宽刻度标签完整显示（教学点：QFontMetrics）----
    const QFont labelFont(QStringLiteral("Microsoft YaHei"), 8);
    painter.setFont(labelFont);
    const QFontMetrics fm(labelFont);
    qreal leftMargin = fm.horizontalAdvance(QStringLiteral("999.9")) + 14.0;
    if (leftMargin < 40.0)
        leftMargin = 40.0;                                 // 保底宽度

    const qreal rightMargin  = 10.0;                       // 右侧留白
    const qreal topMargin    = 22.0;                       // 顶部留白（标题区）
    const qreal bottomMargin = 16.0;                       // 底部留白
    const QRectF plot = rc.adjusted(leftMargin, topMargin, -rightMargin, -bottomMargin);
    if (plot.width() <= 1.0 || plot.height() <= 1.0)
        return;                                            // 控件过小，放弃绘制

    // ---- 1. 绘图区背景（半透明深色，融入主 QSS 深色主题）----
    painter.fillRect(plot, QColor(0x15, 0x20, 0x2b, 200));

    // ---- 2. Y 轴水平网格线与刻度标签（5 条水平线）----
    const int kDivisions = 4;                               // 分 4 段 → 5 条线
    for (int i = 0; i <= kDivisions; ++i) {
        const double frac = double(i) / kDivisions;         // 0=顶(最大值), 1=底(最小值)
        const double value = m_max - frac * (m_max - m_min); // 该网格线对应的量程值
        const qreal y = plot.top() + frac * plot.height();  // 值→像素（线性映射）

        // 网格线：点划线，颜色比刻度标签更淡，突出刻度
        painter.setPen(QPen(QColor(0x3a, 0x4a, 0x5a, 180), 1.0, Qt::DotLine));
        painter.drawLine(QPointF(plot.left(), y), QPointF(plot.right(), y));

        // 刻度标签：左边缘右对齐显示
        painter.setPen(QColor(0x9f, 0xb3, 0xc8));
        painter.drawText(QRectF(0.0, y - 8.0, leftMargin - 6.0, 16.0),
                         Qt::AlignRight | Qt::AlignVCenter,
                         formatNumber(value));
    }

    // ---- 3. 折线（QPainterPath，主色青色 #16a085）----
    const int n = m_data.size();
    if (n == 0) {
        // 空数据：绘图区中央显示淡色提示
        painter.setPen(QColor(0x5a, 0x6a, 0x7a));
        painter.drawText(plot, Qt::AlignCenter, tr("暂无数据"));
    } else {
        // X 方向固定步进：最新点锚定右端，实现"滚动"视觉（教学点：坐标映射）
        const qreal step = (m_maxPoints > 1)
            ? plot.width() / (m_maxPoints - 1)
            : plot.width();

        QPainterPath path;
        for (int i = 0; i < n; ++i) {
            const qreal x = plot.right() - (n - 1 - i) * step;        // 索引→像素
            const qreal y = valueToY(m_data.at(i), m_min, m_max, plot); // 值→像素
            if (i == 0)
                path.moveTo(x, y);
            else
                path.lineTo(x, y);
        }

        if (n == 1) {
            // 单点：路径画不出可见线段，单独画一个圆点
            painter.setPen(Qt::NoPen);
            painter.setBrush(QColor(0x16, 0xa0, 0x85));
            painter.drawEllipse(path.currentPosition(), 3.0, 3.0);
        } else {
            painter.setPen(QPen(QColor(0x16, 0xa0, 0x85), 2.0));
            painter.setBrush(Qt::NoBrush);
            painter.drawPath(path);
        }

        // 末点小圆点：高亮指示当前最新值的位置
        painter.setPen(Qt::NoPen);
        painter.setBrush(QColor(0x1a, 0xbc, 0x9c));
        painter.drawEllipse(path.currentPosition(), 2.5, 2.5);
    }

    // ---- 4. 标题（左上）与最新值（右上）----
    painter.setPen(QColor(0xdc, 0xe5, 0xee));
    painter.setFont(QFont(QStringLiteral("Microsoft YaHei"), 9, QFont::Bold));
    painter.drawText(QRectF(plot.left(), 0.0, plot.width() / 2.0, topMargin),
                     Qt::AlignLeft | Qt::AlignVCenter, m_title);

    if (n > 0) {
        painter.setPen(QColor(0x9f, 0xb3, 0xc8));
        painter.setFont(labelFont);
        const QString latest = tr("最新: %1").arg(formatNumber(m_data.constLast()));
        painter.drawText(QRectF(plot.left() + plot.width() / 2.0, 0.0,
                                plot.width() / 2.0, topMargin),
                         Qt::AlignRight | Qt::AlignVCenter, latest);
    }
}

} // namespace ui
} // namespace datascope
