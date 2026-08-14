// ============================================================
//  升级数据包记录模型实现
//
//  【Qt知识点】Model/View 的通知机制：
//  - beginInsertRows / endInsertRows：插入行前后必须成对调用，
//    View 在 begin 时锁定、在 end 时一次性刷新，避免闪烁。
//  - dataChanged()：某行数据变化时通知 View 更新该单元格。
//  这对应 WPF 中 ObservableCollection 的 CollectionChanged 事件。
// ============================================================
#include "UpgradePacketModel.h"

UpgradePacketModel::UpgradePacketModel(QObject *parent)
    : QAbstractTableModel(parent)
{
}

int UpgradePacketModel::rowCount(const QModelIndex &parent) const
{
    // 顶层模型没有父节点，parent.isValid() 为 true 时返回 0
    if (parent.isValid())
        return 0;
    return m_records.size();
}

int UpgradePacketModel::columnCount(const QModelIndex &parent) const
{
    if (parent.isValid())
        return 0;
    return ColCount;
}

QVariant UpgradePacketModel::data(const QModelIndex &index, int role) const
{
    if (!index.isValid() || index.row() < 0 || index.row() >= m_records.size())
        return QVariant();

    const PacketRecord &rec = m_records.at(index.row());

    // Qt::DisplayRole = View 默认显示的文本（类似 WPF 的 TextBlock 绑定）
    if (role == Qt::DisplayRole)
    {
        switch (index.column())
        {
        case ColIndex:  return rec.index;
        case ColOffset: return QStringLiteral("0x%1").arg(rec.offset, 8, 16, QLatin1Char('0'));
        case ColLength: return rec.length;
        case ColStatus: return rec.status;
        default:        return QVariant();
        }
    }

    // Qt::TextAlignmentRole = 单元格对齐（对比 WPF 的 HorizontalAlignment）
    if (role == Qt::TextAlignmentRole)
        return int(Qt::AlignCenter);

    return QVariant();
}

QVariant UpgradePacketModel::headerData(int section, Qt::Orientation orientation, int role) const
{
    // 水平表头：列标题
    if (orientation == Qt::Horizontal && role == Qt::DisplayRole)
    {
        switch (section)
        {
        case ColIndex:  return QStringLiteral("序号");
        case ColOffset: return QStringLiteral("偏移地址");
        case ColLength: return QStringLiteral("长度(字节)");
        case ColStatus: return QStringLiteral("状态");
        default:        return QVariant();
        }
    }
    return QVariant();
}

void UpgradePacketModel::addPacket(quint32 offset, int length)
{
    // 追加一行：必须先通知模型"即将插入"
    int row = m_records.size();
    beginInsertRows(QModelIndex(), row, row);

    PacketRecord rec;
    rec.index  = row + 1;                    // 序号从 1 开始
    rec.offset = offset;
    rec.length = length;
    rec.status = QStringLiteral("发送中");
    m_records.append(rec);

    endInsertRows();  // 插入完成
}

void UpgradePacketModel::updateStatus(int row, const QString &status)
{
    if (row < 0 || row >= m_records.size())
        return;

    m_records[row].status = status;

    // 通知 View：该行的"状态列"数据已变化
    QModelIndex topLeft     = index(row, ColStatus);
    QModelIndex bottomRight = index(row, ColStatus);
    emit dataChanged(topLeft, bottomRight, QVector<int>() << Qt::DisplayRole);
}

void UpgradePacketModel::clear()
{
    if (m_records.isEmpty())
        return;

    beginResetModel();
    m_records.clear();
    endResetModel();
}
