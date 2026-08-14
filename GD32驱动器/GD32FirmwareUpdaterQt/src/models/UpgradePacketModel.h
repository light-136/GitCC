// ============================================================
//  升级数据包记录模型（Model/View 架构教学演示）
//
//  【Qt知识点】QAbstractTableModel —— Qt 的"数据源"抽象：
//  对应 WPF 的 ObservableCollection<T> + DataGrid/ListView。
//  WPF 是"集合通知界面刷新"；Qt 是"View 主动向 Model 索要数据"。
//  因此必须实现 rowCount / columnCount / data 三个纯虚函数，
//  数据变化时用 beginInsertRows / dataChanged 通知 View 刷新。
//
//  本类驱动界面上的"数据包表格"：每发送一个固件包就加一行，
//  展示 序号 | 偏移 | 长度 | 状态，让升级过程可视化。
// ============================================================
#ifndef UPGRADEPACKETMODEL_H
#define UPGRADEPACKETMODEL_H

#include <QAbstractTableModel>
#include <QList>

// 单个数据包的记录结构
struct PacketRecord {
    int      index;     // 包序号（从 1 开始）
    quint32  offset;    // 文件内偏移地址
    int      length;    // 数据长度（字节）
    QString  status;    // 状态文本（发送中/成功/失败重试/超时重试）
};

class UpgradePacketModel : public QAbstractTableModel
{
    Q_OBJECT

public:
    // 表格列定义
    enum Column {
        ColIndex  = 0,   // 序号
        ColOffset = 1,   // 偏移
        ColLength = 2,   // 长度
        ColStatus = 3,   // 状态
        ColCount  = 4    // 总列数
    };

    explicit UpgradePacketModel(QObject *parent = nullptr);

    // ---- 以下为 QAbstractTableModel 必须实现的三个纯虚函数 ----
    // View（QTableView）渲染时会反复调用它们获取数据
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    int columnCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;

    // 表头数据（列标题）
    QVariant headerData(int section, Qt::Orientation orientation,
                        int role = Qt::DisplayRole) const override;

    // ---- 数据操作接口（供升级引擎/界面调用） ----

    /// <summary>
    /// 添加一个新数据包记录
    /// </summary>
    void addPacket(quint32 offset, int length);

    /// <summary>
    /// 更新指定包的状态（成功/失败重试等）
    /// </summary>
    void updateStatus(int row, const QString &status);

    /// <summary>
    /// 清空所有记录（每次升级前调用）
    /// </summary>
    void clear();

private:
    // 数据记录容器
    QList<PacketRecord> m_records;
};

#endif // UPGRADEPACKETMODEL_H
