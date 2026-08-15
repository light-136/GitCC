/**
 * @file alarmeventmodel.h
 * @brief V3 UI 层 —— 报警事件列表模型（QAbstractListModel）
 *
 * ── 开发思路 ──
 * 报警事件是"事件级"低频数据（触发/恢复边沿才产生一条），与高频实时值不同，它正适合
 * 走 Model/View（权威规格第 10 节：报警事件走 QAbstractListModel + beginInsertRows）。
 * 每次 appendEvent 用 beginInsertRows/endInsertRows 增量通知视图，绝不做整表 reset。
 *
 * 角色设计：提供类型/通道/时间/文本多种角色，QWidget 的 QListView 主要消费 DisplayRole
 * （整行文本）与 ForegroundRole（触发红 / 恢复绿）；其余角色供 QML/自定义委托使用。
 *
 * ── WPF 对照 ──
 *   AlarmEventModel  ↔  WPF 的 ObservableCollection<AlarmEvent> + INotifyCollectionChanged；
 *                        appendEvent 相当于 Add 触发 CollectionChanged，视图增量刷新。
 */

#pragma once

#include <QAbstractListModel>
#include <QVector>

#include "domain/alarmrule.h"   // dscope::domain::AlarmEvent

namespace dscope {
namespace ui {

/**
 * @class AlarmEventModel
 * @brief 报警事件列表模型（低频事件流，增量插入）
 */
class AlarmEventModel : public QAbstractListModel
{
    Q_OBJECT

public:
    /** @brief 自定义角色（供 QML/委托使用；QWidget 主要用 DisplayRole/ForegroundRole） */
    enum Roles {
        TypeRole        = Qt::UserRole + 1,   ///< int：AlarmEvent::Type（Trigger/Recover）
        ChannelRole     = Qt::UserRole + 2,   ///< int：通道号（0 基）
        TimeRole        = Qt::UserRole + 3,   ///< QString：时间文本
        TypeTextRole    = Qt::UserRole + 4,   ///< QString："触发"/"恢复"
        ChannelTextRole = Qt::UserRole + 5,   ///< QString："通道 N"
    };

    explicit AlarmEventModel(QObject *parent = nullptr);

    int rowCount(const QModelIndex &parent = QModelIndex()) const override;
    QVariant data(const QModelIndex &index, int role = Qt::DisplayRole) const override;
    QHash<int, QByteArray> roleNames() const override;

    /** @brief 追加一条报警事件（beginInsertRows 增量通知，带上限保护） */
    void appendEvent(const dscope::domain::AlarmEvent &event);

private:
    QVector<dscope::domain::AlarmEvent> m_events;   ///< 事件存储（时间序）
};

} // namespace ui
} // namespace dscope
