/**
 * @file alarmeventmodel.cpp
 * @brief V3 UI 层 —— 报警事件列表模型实现
 *
 * ── 开发思路 ──
 * appendEvent 带"上限保护"：事件是事件级低频，但长跑数小时也可能堆积成千上万条，
 * 超限时移除最旧一条（头部 O(n)，但只在超限时发生，频率极低，可接受），避免内存无界。
 */

#include "ui/models/alarmeventmodel.h"
#include "ui/theme.h"

#include <QBrush>

namespace dscope {
namespace ui {

namespace {
constexpr int kMaxEvents = 500;   ///< 事件上限（超限移除最旧一条，防长跑内存无界）
}

AlarmEventModel::AlarmEventModel(QObject *parent)
    : QAbstractListModel(parent)
{
}

int AlarmEventModel::rowCount(const QModelIndex &parent) const
{
    if (parent.isValid())
        return 0;   // 列表模型无子层级，父索引非法时才算行
    return m_events.size();
}

QVariant AlarmEventModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() < 0 || index.row() >= m_events.size())
        return QVariant();

    const dscope::domain::AlarmEvent &event = m_events.at(index.row());
    const bool trigger = (event.type == dscope::domain::AlarmEvent::Trigger);

    switch (role) {
    case Qt::DisplayRole:
        return QStringLiteral("[%1] %2  %3")
                .arg(trigger ? QStringLiteral("触发") : QStringLiteral("恢复"))
                .arg(QStringLiteral("通道%1").arg(event.channelIndex + 1))
                .arg(event.ts.toString(QStringLiteral("HH:mm:ss.zzz")));

    case Qt::ForegroundRole:
        // 触发红 / 恢复绿，单一来源 Theme。返回 QBrush（QListView 委托按 ForegroundRole
        // 取文本颜色时以 QBrush 消费，QBrush(QColor) 构造明确、无 QVariant 转换歧义）。
        return QBrush(trigger ? Theme::alarmRed() : Theme::normalGreen());

    case TypeRole:
        return int(event.type);

    case ChannelRole:
        return event.channelIndex;

    case TimeRole:
        return event.ts.toString(QStringLiteral("HH:mm:ss.zzz"));

    case TypeTextRole:
        return trigger ? QStringLiteral("触发") : QStringLiteral("恢复");

    case ChannelTextRole:
        return QStringLiteral("通道%1").arg(event.channelIndex + 1);

    default:
        return QVariant();
    }
}

QHash<int, QByteArray> AlarmEventModel::roleNames() const
{
    QHash<int, QByteArray> roles;
    roles[TypeRole]        = "type";
    roles[ChannelRole]     = "channel";
    roles[TimeRole]        = "time";
    roles[TypeTextRole]    = "typeText";
    roles[ChannelTextRole] = "channelText";
    return roles;
}

void AlarmEventModel::appendEvent(const dscope::domain::AlarmEvent &event)
{
    // 上限保护：超限移除最旧一条（头部移除 O(n)，事件低频，可接受）
    if (m_events.size() >= kMaxEvents) {
        beginRemoveRows(QModelIndex(), 0, 0);
        m_events.removeFirst();
        endRemoveRows();
    }

    // 增量插入到末尾（时间序）
    beginInsertRows(QModelIndex(), m_events.size(), m_events.size());
    m_events.append(event);
    endInsertRows();
}

} // namespace ui
} // namespace dscope
