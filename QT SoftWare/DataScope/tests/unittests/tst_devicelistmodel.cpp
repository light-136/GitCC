/**
 * @file tst_devicelistmodel.cpp
 * @brief 设备列表模型单测（Qt Test + QSignalSpy）
 *
 * 为什么需要这个测试（对应审查"Model/View 层要配单测"）：
 *   Model 是 Qt Model/View 架构的数据源，它的正确性决定整个表格是否可靠。
 *   本套件验证三类行为：
 *     1. 基础数据契约：追加后行数/列数/单元格文本/表头正确；
 *     2. dataChanged 通知：修改状态/通道数必须 emit dataChanged
 *        （这是 View 自动刷新的唯一依据 —— 漏发 = 界面不更新）；
 *     3. 边界与删除：越界访问安全、删除行后行数正确、id 查找命中。
 *
 * 用 QSignalSpy 监听 dataChanged 信号：这是 Model/View 测试的标配手法，
 * 直接断言"信号发出 + 参数范围正确"，比"刷新后肉眼确认"可靠得多。
 */

#include <QtTest>
#include <QSignalSpy>

#include "ui/models/devicelistmodel.h"

using datascope::domain::v2::ConnectionType;
using datascope::domain::v2::DeviceId;
using datascope::domain::v2::DeviceState;
using datascope::ui::models::DeviceListModel;

class TestDeviceListModel : public QObject
{
    Q_OBJECT

private slots:
    void initTestCase();        // 初始化公共测试数据
    void emptyModel_initialState();
    void appendDevice_increasesRowCount();
    void data_cellTextForColumns();
    void headerData_columnNames();
    void setDeviceState_emitsDataChanged();
    void setChannelCount_emitsDataChanged();
    void removeDevice_decreasesRowCount();
    void rowOfId_findsById();
    void clear_resetsModel();
    void outOfRange_returnsNull();
};

// 公共测试数据（追加到模型的多台设备）
DeviceListModel::DeviceItem makeItem(const QString &idText, const QString &name,
                                     ConnectionType type, DeviceState state,
                                     int channels, const QString &addr)
{
    DeviceListModel::DeviceItem item;
    item.id           = DeviceId(idText);
    item.name         = name;
    item.connType     = type;
    item.state        = state;
    item.channelCount = channels;
    item.address      = addr;
    return item;
}

void TestDeviceListModel::initTestCase()
{
}

// ==================== 空模型初始态 ====================

void TestDeviceListModel::emptyModel_initialState()
{
    DeviceListModel model;

    // 空模型：0 行、5 列（列数固定，行数随数据增长）
    QCOMPARE(model.rowCount(), 0);
    QCOMPARE(model.columnCount(), DeviceListModel::ColumnCount);
}

// ==================== 追加设备 → 行数增长 ====================

void TestDeviceListModel::appendDevice_increasesRowCount()
{
    DeviceListModel model;

    const int row0 = model.appendDevice(
        makeItem(QStringLiteral("DEV-1"), QStringLiteral("温度采集仪"),
                 ConnectionType::Tcp, DeviceState::Online, 4,
                 QStringLiteral("127.0.0.1:40001")));
    const int row1 = model.appendDevice(
        makeItem(QStringLiteral("DEV-2"), QStringLiteral("压力记录仪"),
                 ConnectionType::Serial, DeviceState::Disconnected, 2,
                 QStringLiteral("COM3")));

    // 追加顺序即行号：row0=0, row1=1
    QCOMPARE(row0, 0);
    QCOMPARE(row1, 1);
    QCOMPARE(model.rowCount(), 2);
}

// ==================== 单元格文本 ====================

void TestDeviceListModel::data_cellTextForColumns()
{
    DeviceListModel model;
    model.appendDevice(makeItem(QStringLiteral("DEV-1"), QStringLiteral("温度采集仪"),
                                ConnectionType::Tcp, DeviceState::Online, 4,
                                QStringLiteral("127.0.0.1:40001")));

    // 逐列校验 DisplayRole 文本
    QCOMPARE(model.data(model.index(0, DeviceListModel::ColumnName), Qt::DisplayRole),
             QVariant(QStringLiteral("温度采集仪")));
    QCOMPARE(model.data(model.index(0, DeviceListModel::ColumnType), Qt::DisplayRole),
             QVariant(QStringLiteral("TCP")));
    QCOMPARE(model.data(model.index(0, DeviceListModel::ColumnState), Qt::DisplayRole),
             QVariant(QStringLiteral("已连接")));   // deviceStateName(Online)
    QCOMPARE(model.data(model.index(0, DeviceListModel::ColumnChannels), Qt::DisplayRole),
             QVariant(4));
    QCOMPARE(model.data(model.index(0, DeviceListModel::ColumnAddress), Qt::DisplayRole),
             QVariant(QStringLiteral("127.0.0.1:40001")));
}

// ==================== 表头 ====================

void TestDeviceListModel::headerData_columnNames()
{
    DeviceListModel model;

    // 列标题（水平方向）
    QCOMPARE(model.headerData(DeviceListModel::ColumnName, Qt::Horizontal),
             QVariant(QStringLiteral("设备名称")));
    QCOMPARE(model.headerData(DeviceListModel::ColumnState, Qt::Horizontal),
             QVariant(QStringLiteral("状态")));

    // 行头：1 基行号
    QCOMPARE(model.headerData(0, Qt::Vertical), QVariant(1));
    QCOMPARE(model.headerData(1, Qt::Vertical), QVariant(2));
}

// ==================== dataChanged 通知 ====================

void TestDeviceListModel::setDeviceState_emitsDataChanged()
{
    DeviceListModel model;
    model.appendDevice(makeItem(QStringLiteral("DEV-1"), QStringLiteral("设备A"),
                                ConnectionType::Tcp, DeviceState::Disconnected, 0,
                                QStringLiteral("127.0.0.1:40001")));

    // 监听 dataChanged：修改状态必须触发通知（否则 View 不刷新）
    QSignalSpy spy(&model, &DeviceListModel::dataChanged);

    model.setDeviceState(0, DeviceState::Online);

    // 恰好发一次通知，且通知范围只覆盖"状态"那一列
    QCOMPARE(spy.count(), 1);
    const auto args = spy.at(0);
    const QModelIndex topLeft     = qvariant_cast<QModelIndex>(args.at(0));
    const QModelIndex bottomRight = qvariant_cast<QModelIndex>(args.at(1));
    QCOMPARE(topLeft.row(), 0);
    QCOMPARE(topLeft.column(), DeviceListModel::ColumnState);
    QCOMPARE(bottomRight.column(), DeviceListModel::ColumnState);

    // 数据确实更新了
    QCOMPARE(model.data(model.index(0, DeviceListModel::ColumnState), Qt::DisplayRole),
             QVariant(QStringLiteral("已连接")));
}

void TestDeviceListModel::setChannelCount_emitsDataChanged()
{
    DeviceListModel model;
    model.appendDevice(makeItem(QStringLiteral("DEV-1"), QStringLiteral("设备A"),
                                ConnectionType::Tcp, DeviceState::Online, 0,
                                QStringLiteral("127.0.0.1:40001")));

    QSignalSpy spy(&model, &DeviceListModel::dataChanged);

    model.setChannelCount(0, 8);

    QCOMPARE(spy.count(), 1);
    // 通道数列更新
    QCOMPARE(model.data(model.index(0, DeviceListModel::ColumnChannels), Qt::DisplayRole),
             QVariant(8));
}

// ==================== 删除行 ====================

void TestDeviceListModel::removeDevice_decreasesRowCount()
{
    DeviceListModel model;
    model.appendDevice(makeItem(QStringLiteral("DEV-1"), QStringLiteral("设备A"),
                                ConnectionType::Tcp, DeviceState::Online, 4, QStringLiteral("a")));
    model.appendDevice(makeItem(QStringLiteral("DEV-2"), QStringLiteral("设备B"),
                                ConnectionType::Tcp, DeviceState::Online, 2, QStringLiteral("b")));

    // 删除第 0 行：剩 1 行，剩余的是设备B
    QVERIFY(model.removeDevice(0));
    QCOMPARE(model.rowCount(), 1);
    QCOMPARE(model.data(model.index(0, DeviceListModel::ColumnName), Qt::DisplayRole),
             QVariant(QStringLiteral("设备B")));

    // 越界删除返回 false
    QVERIFY(!model.removeDevice(5));
}

// ==================== id 查找 ====================

void TestDeviceListModel::rowOfId_findsById()
{
    DeviceListModel model;
    model.appendDevice(makeItem(QStringLiteral("DEV-1"), QStringLiteral("设备A"),
                                ConnectionType::Tcp, DeviceState::Online, 4, QStringLiteral("a")));
    model.appendDevice(makeItem(QStringLiteral("DEV-2"), QStringLiteral("设备B"),
                                ConnectionType::Tcp, DeviceState::Online, 2, QStringLiteral("b")));

    QCOMPARE(model.rowOfId(DeviceId(QStringLiteral("DEV-2"))), 1);
    QCOMPARE(model.rowOfId(DeviceId(QStringLiteral("DEV-1"))), 0);
    QCOMPARE(model.rowOfId(DeviceId(QStringLiteral("DEV-999"))), -1);  // 不存在
}

// ==================== 清空 ====================

void TestDeviceListModel::clear_resetsModel()
{
    DeviceListModel model;
    model.appendDevice(makeItem(QStringLiteral("DEV-1"), QStringLiteral("设备A"),
                                ConnectionType::Tcp, DeviceState::Online, 4, QStringLiteral("a")));
    model.appendDevice(makeItem(QStringLiteral("DEV-2"), QStringLiteral("设备B"),
                                ConnectionType::Tcp, DeviceState::Online, 2, QStringLiteral("b")));
    QCOMPARE(model.rowCount(), 2);

    model.clear();
    QCOMPARE(model.rowCount(), 0);

    // 清空后追加仍可用（模型可复用）
    model.appendDevice(makeItem(QStringLiteral("DEV-3"), QStringLiteral("设备C"),
                                ConnectionType::Tcp, DeviceState::Online, 1, QStringLiteral("c")));
    QCOMPARE(model.rowCount(), 1);
}

// ==================== 越界安全 ====================

void TestDeviceListModel::outOfRange_returnsNull()
{
    DeviceListModel model;
    model.appendDevice(makeItem(QStringLiteral("DEV-1"), QStringLiteral("设备A"),
                                ConnectionType::Tcp, DeviceState::Online, 4, QStringLiteral("a")));

    // 越界行/列：data 返回无效 QVariant（不崩溃）
    QVERIFY(!model.data(model.index(10, 0)).isValid());
    QVERIFY(!model.data(model.index(0, 99)).isValid());
    // 无效索引
    QVERIFY(!model.data(QModelIndex()).isValid());
}

QTEST_MAIN(TestDeviceListModel)
#include "tst_devicelistmodel.moc"
