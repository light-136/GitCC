/**
 * @file devicelistmodel.cpp
 * @brief 设备列表模型实现
 *
 * 关键机制：beginInsertRows/endInsertRows 与 dataChanged。
 *   - 插入/删除行时必须包裹在 begin*Rows()/end*Rows() 之间：
 *     View 在 begin 时知道"行集合即将变化"，在 end 时知道"变化结束，重排"。
 *     跳过这两个调用是 Qt Model/View 最常见的错误 —— View 缓存的行数
 *     与真实行数不一致，轻则显示错位、重则越界崩溃。
 *   - 修改已有行用 dataChanged(topLeft, bottomRight)：只通知受影响的范围，
 *     View 只重绘那几格，性能好、语义准。
 *
 * 与 V1 的对比（教学点）：V1 是"数据来了一口 setText 到 Label"；
 * 这里改成了"数据变了 emit dataChanged，View 自己来取"——
 * 这正是 WPF Binding 与 Qt Model/View 共同的核心思想：数据驱动界面。
 */

#include "ui/models/devicelistmodel.h"

#include <QtGlobal>

namespace datascope {
namespace ui {
namespace models {

DeviceListModel::DeviceListModel(QObject *parent)
    : QAbstractTableModel(parent)
{
}

int DeviceListModel::rowCount(const QModelIndex &parent) const
{
    // 顶层（parent 无效）返回设备数量；子节点一律 0（本模型是平坦表格，无层级）
    if (parent.isValid())
        return 0;
    return m_items.size();
}

int DeviceListModel::columnCount(const QModelIndex &parent) const
{
    // 列数固定为 ColumnCount（5 列），与 headerData 一一对应
    if (parent.isValid())
        return 0;
    return ColumnCount;
}

QVariant DeviceListModel::data(const QModelIndex &index, int role) const
{
    // 越界保护：行/列超出实际范围直接返回空（View 的防御性编程靠这个兜底）
    if (!index.isValid() || index.row() >= m_items.size())
        return QVariant();

    const DeviceItem &item = m_items.at(index.row());

    if (role == Qt::DisplayRole) {
        // 按列返回对应文本 —— 每列一个属性，集中在这里映射
        switch (index.column()) {
        case ColumnName:     return item.name;
        case ColumnType:     return item.connType == datascope::domain::v2::ConnectionType::Tcp
                                     ? QStringLiteral("TCP")
                                     : QStringLiteral("串口");
        case ColumnState:    return datascope::domain::v2::deviceStateName(item.state);
        case ColumnChannels: return item.channelCount;
        case ColumnAddress:  return item.address;
        default:             return QVariant();
        }
    }

    if (role == Qt::TextAlignmentRole) {
        // 数值列右对齐、文本列左对齐 —— 视觉习惯
        switch (index.column()) {
        case ColumnChannels: return QVariant(Qt::AlignRight | Qt::AlignVCenter);
        default:             return QVariant(Qt::AlignLeft | Qt::AlignVCenter);
        }
    }

    return QVariant();
}

QVariant DeviceListModel::headerData(int section, Qt::Orientation orientation,
                                     int role) const
{
    if (role != Qt::DisplayRole)
        return QVariant();

    if (orientation == Qt::Horizontal) {
        // 列标题：与 Column 枚举对齐（新增列务必同步这里）
        switch (section) {
        case ColumnName:     return tr("设备名称");
        case ColumnType:     return tr("连接方式");
        case ColumnState:    return tr("状态");
        case ColumnChannels: return tr("通道数");
        case ColumnAddress:  return tr("连接地址");
        default:             return QVariant();
        }
    }

    // 行头显示 1 基行号（人类习惯从 1 开始，内部下标从 0）
    return section + 1;
}

int DeviceListModel::appendDevice(const DeviceItem &item)
{
    // beginInsertRows 必须传"插入区间"：从 m_items.size() 到 size()（新加 1 行）
    const int row = m_items.size();
    beginInsertRows(QModelIndex(), row, row);
    m_items.append(item);
    endInsertRows();
    return row;
}

bool DeviceListModel::removeDevice(int row)
{
    if (row < 0 || row >= m_items.size())
        return false;

    beginRemoveRows(QModelIndex(), row, row);
    m_items.removeAt(row);
    endRemoveRows();
    return true;
}

void DeviceListModel::setDeviceState(int row, datascope::domain::v2::DeviceState state)
{
    if (row < 0 || row >= m_items.size())
        return;

    // 先改数据，再通知 View 该行变化（列是固定下标 ColumnState）
    m_items[row].state = state;
    const QModelIndex topLeft     = index(row, ColumnState);
    const QModelIndex bottomRight = index(row, ColumnState);
    emit dataChanged(topLeft, bottomRight);
}

void DeviceListModel::setChannelCount(int row, int count)
{
    if (row < 0 || row >= m_items.size())
        return;

    m_items[row].channelCount = count;
    const QModelIndex topLeft     = index(row, ColumnChannels);
    const QModelIndex bottomRight = index(row, ColumnChannels);
    emit dataChanged(topLeft, bottomRight);
}

int DeviceListModel::rowOfId(const datascope::domain::v2::DeviceId &id) const
{
    // 线性查找：设备数量通常个位数~几十台，O(n) 足够；id 才是稳定身份
    for (int i = 0; i < m_items.size(); ++i) {
        if (m_items.at(i).id == id)
            return i;
    }
    return -1;
}

void DeviceListModel::clear()
{
    if (m_items.isEmpty())
        return;
    beginResetModel();
    m_items.clear();
    endResetModel();
}

} // namespace models
} // namespace ui
} // namespace datascope
