/**
 * @file linechartwidget.cpp
 * @brief V3 UI 层 —— 多通道滚动曲线控件实现
 *
 * ── 开发思路 ──
 * paintEvent 分两层：先贴静态图层缓存（背景/网格/阈值线/图例，重建昂贵但低频），
 * 再逐通道把内存缓冲映射成像素折线（每帧都做，但只做这一件事）。Y 值越界（<0 或
 * >100）会被裁剪到量程内，避免折线画出绘图区。
 */

#include "ui/widgets/linechartwidget.h"
#include "ui/theme.h"

#include <QPainter>
#include <QPaintEvent>
#include <QPolygon>
#include <QResizeEvent>
#include <limits>

namespace dscope {
namespace ui {

namespace {

// 绘图区边距（像素）：左给 Y 刻度文字、下给预留、右上留呼吸空间
constexpr int kMarginLeft   = 44;
constexpr int kMarginRight  = 14;
constexpr int kMarginTop    = 14;
constexpr int kMarginBottom = 26;

// Y 量程固定 0~100（权威规格：量程 0~100）
constexpr double kRangeMin = 0.0;
constexpr double kRangeMax = 100.0;

/** @brief 依据控件尺寸计算绘图区矩形（扣除四周边距） */
QRectF plotRect(const QSize &sz)
{
    return QRectF(kMarginLeft, kMarginTop,
                  sz.width() - kMarginLeft - kMarginRight,
                  sz.height() - kMarginTop - kMarginBottom);
}

/** @brief 把工程值裁剪到量程内（越界值不画出绘图区） */
double clampToRange(double v)
{
    if (v < kRangeMin) return kRangeMin;
    if (v > kRangeMax) return kRangeMax;
    return v;
}

} // namespace

LineChartWidget::LineChartWidget(QWidget *parent)
    : QWidget(parent)
{
    // 深色背景（通过调色板，不写裸 hex）
    QPalette pal = palette();
    pal.setColor(QPalette::Window, Theme::background());
    setPalette(pal);
    setAutoFillBackground(true);

    setMinimumSize(320, 180);
}

void LineChartWidget::setChannelCount(int n)
{
    if (n < 0)
        n = 0;
    if (n == m_channelCount)
        return;

    m_channelCount = n;
    m_data.resize(n);
    m_thresholds.resize(n);
    m_hasThreshold.resize(n);
    for (int i = 0; i < n; ++i) {
        m_thresholds[i]   = std::numeric_limits<double>::quiet_NaN();   // NaN 表示"未设置阈值"
        m_hasThreshold[i] = false;
    }

    m_bgCache = QPixmap();   // 失效静态图层缓存
    update();
}

void LineChartWidget::setValue(int channelIndex, double value)
{
    if (channelIndex < 0 || channelIndex >= m_channelCount)
        return;

    std::deque<double> &buffer = m_data[channelIndex];
    buffer.push_back(value);
    // 环形窗口：超出容量丢弃最旧点。deque 的 push_back/pop_front 两端 O(1)，
    // 规避 V2 用 QVector::removeFirst() 每点整体前移的 O(n) 性能债。
    if (int(buffer.size()) > m_capacity)
        buffer.pop_front();

    update();   // Qt 自动合并同一事件循环内的多次 update() 为一次 paintEvent
}

void LineChartWidget::setThreshold(int channelIndex, double threshold)
{
    if (channelIndex < 0 || channelIndex >= m_channelCount)
        return;

    m_thresholds[channelIndex]   = threshold;
    m_hasThreshold[channelIndex] = true;

    m_bgCache = QPixmap();   // 阈值虚线画在静态图层里，需重建缓存
    update();
}

void LineChartWidget::clear()
{
    for (std::deque<double> &buffer : m_data)
        buffer.clear();

    m_bgCache = QPixmap();   // 图例/阈值虽不变，但为保险一并重建（清空后折线消失）
    update();
}

void LineChartWidget::resizeEvent(QResizeEvent *event)
{
    m_bgCache = QPixmap();   // 尺寸变化 → 静态图层必须按新尺寸重建
    QWidget::resizeEvent(event);
}

void LineChartWidget::paintEvent(QPaintEvent *event)
{
    Q_UNUSED(event);

    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing, true);

    // 1) 静态图层缓存（懒重建）
    if (m_bgCache.isNull() || m_bgCache.size() != size())
        rebuildBackgroundCache();
    if (!m_bgCache.isNull())
        painter.drawPixmap(0, 0, m_bgCache);

    // 2) 逐通道画折线（每帧唯一重算的昂贵部分）
    const QRectF plot = plotRect(size());
    const double dx   = plot.width() / (m_capacity - 1);   // 每格水平像素

    // Y：工程值 → 像素（0 在底，100 在顶）
    auto yForValue = [&](double v) {
        const double c = clampToRange(v);
        return plot.bottom() - (c - kRangeMin) / (kRangeMax - kRangeMin) * plot.height();
    };
    // X：样本序号 → 像素（最新点恒在右缘，数据增多整体左移 = 滚动）
    auto xForIndex = [&](int i, int count) {
        return plot.right() - (count - 1 - i) * dx;
    };

    for (int ch = 0; ch < m_channelCount; ++ch) {
        const std::deque<double> &buffer = m_data.at(ch);
        const int count = int(buffer.size());
        if (count < 2)
            continue;   // 少于 2 点画不出折线

        QPolygonF poly;
        poly.reserve(count);   // 预分配，避免逐点扩容
        int i = 0;
        for (double v : buffer) {
            poly << QPointF(xForIndex(i, count), yForValue(v));
            ++i;
        }

        painter.setPen(QPen(Theme::curveChannel(ch), 1.6));
        painter.drawPolyline(poly);
    }
}

void LineChartWidget::rebuildBackgroundCache()
{
    if (width() <= 0 || height() <= 0)
        return;

    QPixmap cache(size());
    cache.fill(Qt::transparent);

    QPainter painter(&cache);
    painter.setRenderHint(QPainter::Antialiasing, true);

    // 背景
    painter.fillRect(rect(), Theme::background());

    const QRectF plot = plotRect(size());

    // 绘图区面板背景
    painter.fillRect(plot, Theme::panelBackground());

    painter.setFont(Theme::axisFont());

    // 水平网格 + Y 刻度（0/20/40/60/80/100）
    const int hSegments = 5;
    for (int i = 0; i <= hSegments; ++i) {
        const double value = kRangeMin + (kRangeMax - kRangeMin) * i / hSegments;
        const double y = plot.bottom() - (value - kRangeMin) / (kRangeMax - kRangeMin) * plot.height();

        painter.setPen(Theme::gridLine());
        painter.drawLine(QPointF(plot.left(), y), QPointF(plot.right(), y));

        painter.setPen(Theme::axisText());
        painter.drawText(QRectF(0, y - 8, kMarginLeft - 6, 16),
                         Qt::AlignRight | Qt::AlignVCenter,
                         QString::number(value, 'f', 0));
    }

    // 垂直网格（无标签，仅作背景参照）
    const int vSegments = 6;
    for (int i = 0; i <= vSegments; ++i) {
        const double x = plot.left() + plot.width() * i / vSegments;
        painter.setPen(Theme::gridLine());
        painter.drawLine(QPointF(x, plot.top()), QPointF(x, plot.bottom()));
    }

    // 绘图区边框
    painter.setPen(Theme::border());
    painter.setBrush(Qt::NoBrush);
    painter.drawRect(plot);

    // 阈值虚线（每通道用各自曲线色区分，一次性注入、非每帧）
    for (int ch = 0; ch < m_channelCount; ++ch) {
        if (!m_hasThreshold.at(ch))
            continue;
        const double y = plot.bottom() - (clampToRange(m_thresholds.at(ch)) - kRangeMin)
                                         / (kRangeMax - kRangeMin) * plot.height();
        painter.setPen(QPen(Theme::curveChannel(ch), 1.2, Qt::DashLine));
        painter.drawLine(QPointF(plot.left(), y), QPointF(plot.right(), y));
    }

    // 图例（通道色块 + CHn），左上角，供多通道区分
    double lx = plot.left() + 6;
    const double ly = plot.top() + 4;
    for (int ch = 0; ch < m_channelCount; ++ch) {
        const QColor color = Theme::curveChannel(ch);
        painter.setPen(color);
        painter.setBrush(color);
        painter.drawRect(QRectF(lx, ly, 10, 10));

        painter.setPen(Theme::textPrimary());
        painter.drawText(QRectF(lx + 14, ly - 2, 40, 14),
                         Qt::AlignLeft | Qt::AlignVCenter,
                         QStringLiteral("CH%1").arg(ch));
        lx += 54;
    }

    m_bgCache = cache;
}

} // namespace ui
} // namespace dscope
