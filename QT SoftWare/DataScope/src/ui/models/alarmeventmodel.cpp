/**
 * @file alarmeventmodel.cpp
 * @brief 报警事件列表模型实现
 *
 * 关键机制与 DeviceListModel 一致（beginInsertRows / dataChanged），
 * 但这是"列表模型"：没有列概念，只有一行一行的条目。
 * 报警文本的拼装集中在这里（displayText()），UI 只需 setModel + QListView，
 * 不需要知道"报警长什么样"——展示细节归 Model 管。
 */

#include "ui/models/alarmeventmodel.h"

#include <QModelIndex>

namespace datascope {
namespace ui {
namespace models {

AlarmEventModel::AlarmEventModel(QObject *parent)
    : QAbstractListModel(parent)
{
}

int AlarmEventModel::rowCount(const QModelIndex &parent) const
{
    if (parent.isValid())
        return 0;
    return m_events.size();
}

QVariant AlarmEventModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() >= m_events.size())
        return QVariant();

    const datascope::domain::v2::AlarmEvent &ev = m_events.at(index.row());

    if (role == Qt::DisplayRole) {
        // 组装一行报警文本：[时间] [级别] 通道N 描述 [(已确认)]
        QString text = QStringLiteral("[%1] [%2] 通道%3 %4")
                           .arg(ev.time().toString(QStringLiteral("HH:mm:ss")),
                                severityName(ev.severity()),
                                QString::number(ev.channelIndex() + 1),
                                ev.message());
        if (ev.isAcked())
            text += QStringLiteral(" (已确认)");
        return text;
    }

    return QVariant();
}

QString AlarmEventModel::severityName(datascope::domain::v2::AlarmSeverity severity) const
{
    // 级别中文名集中定义，供 data() 与未来 DecorationRole 复用
    switch (severity) {
    case datascope::domain::v2::AlarmSeverity::Info:     return QStringLiteral("提示");
    case datascope::domain::v2::AlarmSeverity::Warning:  return QStringLiteral("警告");
    case datascope::domain::v2::AlarmSeverity::Critical: return QStringLiteral("严重");
    }
    return QStringLiteral("未知");
}

void AlarmEventModel::appendEvent(const datascope::domain::v2::AlarmEvent &event)
{
    // 新报警插到最上方（工业监控惯例：最新事件最醒目）
    beginInsertRows(QModelIndex(), 0, 0);
    m_events.prepend(event);
    endInsertRows();
}

void AlarmEventModel::setAcked(int row)
{
    if (row < 0 || row >= m_events.size())
        return;

    // 先确认领域事件（幂等：已确认则 no-op），再通知 View 更新该行
    m_events[row].ack(QDateTime::currentDateTime());
    const QModelIndex idx = index(row);
    emit dataChanged(idx, idx);
}

void AlarmEventModel::clear()
{
    if (m_events.isEmpty())
        return;
    beginResetModel();
    m_events.clear();
    endResetModel();
}

} // namespace models
} // namespace ui
} // namespace datascope
