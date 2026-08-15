/**
 * @file devicelistmodel.h
 * @brief V2 设备列表模型（Qt Model/View 补课核心）
 *
 * ────────────────────────────────────────────────────────────
 * 为什么需要这个类（对应审查 P0-2：全工程无任何 QAbstractTableModel）
 * ────────────────────────────────────────────────────────────
 * V1 里"设备列表"只是页面上的占位文字，没有任何数据展示。
 * Qt 的 Model/View 架构 = WPF 的 DataGrid + ObservableCollection：
 *   数据（Model）与展示（View）分离，Model 变化通过 dataChanged() 通知 View
 *   自动刷新 —— 而不是像 V1 那样"拿到数据后手动 setText 到每个控件"。
 *
 * 本类是设备列表的"数据提供者"，扮演三重角色：
 *   1. 对 View（QTableView）暴露行列结构：rowCount / columnCount / data；
 *   2. 对上层（DeviceManager 服务）暴露数据修改入口：appendDevice /
 *      removeDevice / setDeviceStatus —— 每次修改都 emit dataChanged，
 *      让 View 自动重绘那一格/那一行；
 *   3. 承载列标题（headerData），供 QTableView 显示表头。
 *
 * ── WPF 对照 ──
 *   QAbstractTableModel      ↔   ObservableCollection<T> + INotifyPropertyChanged
 *   dataChanged() 信号       ↔   PropertyChanged / CollectionChanged 事件
 *   QTableView + setModel()  ↔   DataGrid.ItemsSource = collection
 *   headerData()             ↔   DataGrid 列标题 / DataGridTextColumn.Header
 *   beginInsertRows/endInsertRows ↔  ObservableCollection.Add（NotifyCollectionChanged）
 *
 * ── Qt 知识要点 ──
 *   - beginInsertRows()/endInsertRows() 必须在真正插入数据前/后调用：
 *     View 靠这两次调用知道"有新行来了，重新布局"，漏掉会导致 View 与数据不同步。
 *   - data() 的 role 参数：DisplayRole 是显示文本，TextAlignmentRole 是对齐。
 *   - 本类只依赖 QtCore + V2 领域类型，不依赖任何 UI 控件 —— 可以独立单测。
 */

#pragma once

#include <QAbstractTableModel>

#include "domain/domainmodel_v2.h"

namespace datascope {
namespace ui {
namespace models {

/**
 * @class DeviceListModel
 * @brief 设备列表的表格模型（每行一台设备，每列一个属性）
 *
 * 列定义（枚举替代魔法数，方便列索引与表头一一对应）：
 *   ColumnName     设备名称
 *   ColumnType     连接方式（TCP / 串口）
 *   ColumnState    连接状态（未连接 / 连接中 / 已连接 / 出错）
 *   ColumnChannels 通道数
 *   ColumnAddress  连接地址（TCP: host:port；串口: COM口）
 */
class DeviceListModel : public QAbstractTableModel
{
    Q_OBJECT

public:
    /** @brief 表格列索引（新增列时在此追加，保持与 headerData 一致） */
    enum Column {
        ColumnName = 0,
        ColumnType,
        ColumnState,
        ColumnChannels,
        ColumnAddress,
        ColumnCount   ///< 列数（枚举哨兵值，不要直接使用）
    };

    /** @brief 一行设备的完整信息（来自 DeviceManager 的展示快照） */
    struct DeviceItem {
        datascope::domain::v2::DeviceId   id;        ///< 设备唯一标识
        QString                            name;     ///< 设备名称
        datascope::domain::v2::ConnectionType connType; ///< 连接方式
        datascope::domain::v2::DeviceState state;    ///< 连接状态
        int                                channelCount = 0; ///< 通道数
        QString                            address;   ///< 连接地址文本
    };

    /** @brief 构造空模型 */
    explicit DeviceListModel(QObject *parent = nullptr);

    // ---- QAbstractTableModel 纯虚/虚函数实现（View 调用）----

    /** @brief 行数 = 设备数量 */
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;

    /** @brief 列数 = 固定 5 列（见 Column 枚举） */
    int columnCount(const QModelIndex &parent = QModelIndex()) const override;

    /**
     * @brief 取单元格数据
     * @param index  单元格位置（row=设备下标, column=Column 枚举）
     * @param role   DisplayRole 返回文本；TextAlignmentRole 返回对齐方式
     */
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;

    /** @brief 表头：列名（如"设备名称"），行头显示设备序号 */
    QVariant headerData(int section, Qt::Orientation orientation,
                        int role = Qt::DisplayRole) const override;

    // ---- 数据修改入口（上层服务调用；每次修改触发 dataChanged）----

    /** @brief 追加一台设备（返回新行号；失败返回 -1） */
    int appendDevice(const DeviceItem &item);

    /** @brief 移除指定行设备；成功返回 true */
    bool removeDevice(int row);

    /** @brief 更新指定行设备的连接状态（View 自动刷新该行） */
    void setDeviceState(int row, datascope::domain::v2::DeviceState state);

    /** @brief 更新指定行设备的通道数 */
    void setChannelCount(int row, int count);

    /** @brief 按 id 查找行号（找不到返回 -1） */
    int rowOfId(const datascope::domain::v2::DeviceId &id) const;

    /**
     * @brief 读取指定行设备信息（只读查询）
     * @param row 行号
     * @return 该行 DeviceItem 拷贝；越界返回默认构造（调用方应先判 isValid）
     */
    DeviceItem itemAt(int row) const;

    /** @brief 清空全部设备 */
    void clear();

private:
    QVector<DeviceItem> m_items;   ///< 设备数据容器（Model 的真正数据源）
};

} // namespace models
} // namespace ui
} // namespace datascope
