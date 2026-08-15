/**
 * @file alarmeventmodel.h
 * @brief V2 报警事件模型（Qt Model/View —— QAbstractListModel）
 *
 * ────────────────────────────────────────────────────────────
 * 为什么需要这个类（对应审查 P0-3：报警业务逻辑在 View、无报警中心）
 * ────────────────────────────────────────────────────────────
 * V1 的"报警"只是 monitorpage 里一行 value > max*0.85 的内联代码，
 * 没有报警事件、没有报警列表、没有确认机制。
 * V2 用 AlarmEngine（服务层，阶段 D）产生 AlarmEvent，本 Model 负责
 * 把这些事件展示成可滚动列表（QListView 消费）——报警中心的数据源。
 *
 * 与 DeviceListModel 的区别（QAbstractTableModel vs QAbstractListModel）：
 *   - 表格模型（设备列表）：多列，关注"每列一个属性"；
 *   - 列表模型（报警中心）：单列，关注"每行一条报警事件的完整描述"。
 *
 * ── WPF 对照 ──
 *   QAbstractListModel      ↔   ObservableCollection<AlarmEvent>
 *   dataChanged / beginReset ↔   CollectionChanged
 *   appendEvent()           ↔   ObservableCollection.Add
 */

#pragma once

#include <QAbstractListModel>

#include "domain/domainmodel_v2.h"

namespace datascope {
namespace ui {
namespace models {

/**
 * @class AlarmEventModel
 * @brief 报警事件列表模型（每行一条报警事件）
 *
 * 每条报警显示一行汇总文本，格式：
 *   [时间 HH:mm:ss] [级别] 通道N 描述
 *   例：[10:00:00] [严重] 通道2 温度过高
 *
 * 设计思路：
 *   - appendEvent() 从 AlarmEngine 收到新事件时调用，自动触发行插入通知；
 *   - setAcked() 确认后更新该行显示（追加"(已确认)"标记）并 dataChanged；
 *   - 支持 clear()（报警中心"清除全部"）。
 */
class AlarmEventModel : public QAbstractListModel
{
    Q_OBJECT

public:
    explicit AlarmEventModel(QObject *parent = nullptr);

    /** @brief 行数 = 报警事件条数 */
    int rowCount(const QModelIndex &parent = QModelIndex()) const override;

    /**
     * @brief 取行数据
     * DisplayRole 返回汇总文本；DecorationRole 预留（未来可用图标区分级别）
     */
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;

    /** @brief 追加一条报警事件（头部插入：最新报警在最上方，符合监控习惯） */
    void appendEvent(const datascope::domain::v2::AlarmEvent &event);

    /** @brief 按行号确认报警（未确认 → 已确认，更新显示） */
    void setAcked(int row);

    /** @brief 清空全部报警 */
    void clear();

    /** @brief 当前报警条数 */
    int count() const { return m_events.size(); }

private:
    /** @brief 报警级别 → 中文名（供 data() 拼装显示文本） */
    QString severityName(datascope::domain::v2::AlarmSeverity severity) const;

    QVector<datascope::domain::v2::AlarmEvent> m_events;  ///< 报警事件容器
};

} // namespace models
} // namespace ui
} // namespace datascope
