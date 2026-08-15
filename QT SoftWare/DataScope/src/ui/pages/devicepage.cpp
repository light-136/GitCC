/**
 * @file devicepage.cpp
 * @brief 设备管理页实现（V2：真实设备配置/管理/连接）
 *
 * ────────────────────────────────────────────────────────────
 * 页面职责与数据流
 * ────────────────────────────────────────────────────────────
 *   1. 展示：QTableView ← DeviceListModel（V2 Model/View 补课成果）；
 *   2. 持久化：设备配置 JSON 数组 → ConfigManager（INI 文件，重启加载）；
 *   3. 连接：选中设备 → DataService.connectTo(host, port) + startAcquisition()，
 *      connected/disconnected/connectionError 信号回写状态列。
 *
 * 设备配置的"唯一 owner"是页面自己（m_configs）：
 *   - DeviceListModel 的 DeviceItem 只保存展示快照（address 是拼好的文本）；
 *   - 连接需要 host/port 分开 —— 所以每次"行 ↔ id"由 model 的 itemAt()/rowOfId()
 *     关联，id ↔ 完整配置由 m_configs 关联，两份数据以 id 为外键同步。
 *
 * 对应 WPF 对照：
 *   QTableView + DeviceListModel   ↔  DataGrid + ObservableCollection<DeviceVM>
 *   m_configs（配置唯一 owner）     ↔  ViewModel 持有的完整实体集合
 *   DataService.connectTo()        ↔  Repository/Service 层连接方法
 *   onServiceConnected() 槽        ↔  事件订阅（连接结果回写属性）
 */

#include "ui/pages/devicepage.h"

#include "ui/models/devicelistmodel.h"
#include "services/dataservice.h"
#include "infrastructure/configmanager.h"

#include <QTableView>
#include <QHeaderView>
#include <QLineEdit>
#include <QSpinBox>
#include <QPushButton>
#include <QLabel>
#include <QFormLayout>
#include <QHBoxLayout>
#include <QVBoxLayout>
#include <QMessageBox>
#include <QJsonDocument>
#include <QJsonArray>
#include <QJsonObject>

namespace datascope {
namespace ui {

namespace {
// 设备配置在 ConfigManager 中的键名（配置分组常量，避免魔法字符串散落）
const QString kDevicesKey = QStringLiteral("devices");
// 默认设备参数（模拟设备默认监听 0.0.0.0:40001）
const int kDefaultPort = 40001;
const int kDefaultTimeoutMs = 5000;
const int kDefaultChannels = 4;
} // namespace

DevicePage::DevicePage(datascope::services::DataService *dataService, QWidget *parent)
    : QWidget(parent)
    , m_dataService(dataService)
{
    // 数据总线必须注入（连接/断开全靠它驱动），空指针是编程错误
    Q_ASSERT(m_dataService);

    setObjectName(QStringLiteral("devicePage"));
    buildUi();

    // ---- DataService 信号回写：连接结果 → 状态列 ----
    // 连接是异步的，结果由信号通知；页面据此更新选中设备的状态
    connect(m_dataService, &datascope::services::DataService::connected,
            this, &DevicePage::onServiceConnected);
    connect(m_dataService, &datascope::services::DataService::disconnected,
            this, &DevicePage::onServiceDisconnected);
    connect(m_dataService, &datascope::services::DataService::connectionError,
            this, &DevicePage::onServiceError);

    // 启动时从持久化存储加载设备列表（重启不丢失）
    loadDevices();
}

// ---------------------------------------------------------------------------
// UI 构建
// ---------------------------------------------------------------------------

void DevicePage::buildUi()
{
    auto *layout = new QHBoxLayout(this);

    // 左侧：设备列表（占约 60% 宽度）
    buildDeviceTable();
    layout->addWidget(m_tableView, 3);

    // 右侧：配置表单 + 操作按钮（占约 40% 宽度）
    auto *formPanel = buildForm();
    layout->addWidget(formPanel, 2);
}

void DevicePage::buildDeviceTable()
{
    m_model = new datascope::ui::models::DeviceListModel(this);

    m_tableView = new QTableView(this);
    m_tableView->setModel(m_model);
    m_tableView->setSelectionBehavior(QAbstractItemView::SelectRows);  // 整行选中
    m_tableView->setSelectionMode(QAbstractItemView::SingleSelection); // 单选
    m_tableView->setEditTriggers(QAbstractItemView::NoEditTriggers);   // 只读展示
    m_tableView->setAlternatingRowColors(true);                        // 斑马纹，工业惯例
    m_tableView->horizontalHeader()->setStretchLastSection(true);      // 地址列拉伸
    m_tableView->horizontalHeader()->setSectionResizeMode(QHeaderView::ResizeToContents);

    // 选中行变化 → 同步按钮可用性（无选中则删除/连接/断开都不可点）
    connect(m_tableView->selectionModel(), &QItemSelectionModel::selectionChanged,
            this, &DevicePage::onSelectionChanged);
}

QWidget *DevicePage::buildForm()
{
    // 表单面板：顶部标题 → 输入区 → 按钮区 → 状态提示区
    auto *panel = new QWidget(this);
    auto *form = new QVBoxLayout(panel);

    auto *title = new QLabel(tr("设备配置"), panel);
    title->setObjectName(QStringLiteral("pageTitle"));
    title->setAlignment(Qt::AlignCenter);
    form->addWidget(title);

    // ---- 输入区：表单布局（标签 | 控件 两列）----
    m_nameEdit   = new QLineEdit(panel);
    m_nameEdit->setPlaceholderText(tr("如：主站-01"));
    m_hostEdit   = new QLineEdit(panel);
    m_hostEdit->setPlaceholderText(tr("如：127.0.0.1"));
    m_portSpin   = new QSpinBox(panel);
    m_portSpin->setRange(1, 65535);
    m_portSpin->setValue(kDefaultPort);
    m_timeoutSpin = new QSpinBox(panel);
    m_timeoutSpin->setRange(100, 60000);
    m_timeoutSpin->setSingleStep(500);
    m_timeoutSpin->setValue(kDefaultTimeoutMs);
    m_timeoutSpin->setSuffix(tr(" ms"));
    m_channelSpin = new QSpinBox(panel);
    m_channelSpin->setRange(1, 64);
    m_channelSpin->setValue(kDefaultChannels);

    auto *fields = new QFormLayout;
    fields->addRow(tr("设备名称："), m_nameEdit);
    fields->addRow(tr("主机地址："), m_hostEdit);
    fields->addRow(tr("端口号："),   m_portSpin);
    fields->addRow(tr("连接超时："), m_timeoutSpin);
    fields->addRow(tr("通道数："),   m_channelSpin);
    form->addLayout(fields);

    // ---- 操作按钮区 ----
    m_addBtn = new QPushButton(tr("添加设备"), panel);
    m_addBtn->setDefault(true);              // 回车触发添加
    m_removeBtn = new QPushButton(tr("删除选中"), panel);
    m_removeBtn->setEnabled(false);          // 初始无选中：禁用
    m_connectBtn = new QPushButton(tr("连接选中"), panel);
    m_connectBtn->setEnabled(false);
    m_disconnectBtn = new QPushButton(tr("断开连接"), panel);
    m_disconnectBtn->setEnabled(false);

    connect(m_addBtn, &QPushButton::clicked, this, &DevicePage::onAddDevice);
    connect(m_removeBtn, &QPushButton::clicked, this, &DevicePage::onRemoveDevice);
    connect(m_connectBtn, &QPushButton::clicked, this, &DevicePage::onConnect);
    connect(m_disconnectBtn, &QPushButton::clicked, this, &DevicePage::onDisconnect);

    form->addWidget(m_addBtn);
    form->addWidget(m_removeBtn);
    form->addWidget(m_connectBtn);
    form->addWidget(m_disconnectBtn);

    // ---- 状态提示区（校验错误 / 连接结果）----
    m_statusLabel = new QLabel(tr("请配置设备参数后添加设备"), panel);
    m_statusLabel->setWordWrap(true);
    m_statusLabel->setObjectName(QStringLiteral("pagePlaceholder"));
    form->addWidget(m_statusLabel, 1);

    return panel;
}

// ---------------------------------------------------------------------------
// 用户动作
// ---------------------------------------------------------------------------

void DevicePage::onAddDevice()
{
    // 表单校验：空名称/空地址直接拒绝并提示（不写入任何数据）
    const QString name = m_nameEdit->text().trimmed();
    const QString host = m_hostEdit->text().trimmed();
    if (name.isEmpty()) {
        m_statusLabel->setText(tr("错误：设备名称不能为空"));
        m_nameEdit->setFocus();
        return;
    }
    if (host.isEmpty()) {
        m_statusLabel->setText(tr("错误：主机地址不能为空"));
        m_hostEdit->setFocus();
        return;
    }

    // 构建设备配置：新 id 保证身份唯一（删除重建不冲突）
    DeviceConfig cfg;
    cfg.id        = datascope::domain::v2::DeviceId::create();
    cfg.name      = name;
    cfg.host      = host;
    cfg.port      = static_cast<quint16>(m_portSpin->value());
    cfg.timeoutMs = m_timeoutSpin->value();
    cfg.channels  = m_channelSpin->value();

    // 写入 Model（展示） + 本页配置（连接用） + 持久化（重启不丢）
    datascope::ui::models::DeviceListModel::DeviceItem item;
    item.id          = cfg.id;
    item.name        = cfg.name;
    item.connType    = datascope::domain::v2::ConnectionType::Tcp;
    item.state       = datascope::domain::v2::DeviceState::Disconnected;
    item.channelCount = cfg.channels;
    item.address     = QStringLiteral("%1:%2").arg(cfg.host).arg(cfg.port);
    m_model->appendDevice(item);
    m_configs.append(cfg);
    saveDevices();

    m_statusLabel->setText(tr("已添加设备：%1").arg(name));
    m_nameEdit->clear();
    m_hostEdit->clear();
    m_nameEdit->setFocus();
}

void DevicePage::onRemoveDevice()
{
    const int row = currentRow();
    if (row < 0)
        return;

    // 先从本页配置删除（按行定位 id），再从 Model 删除 —— 顺序保证 id 关联不悬空
    const datascope::domain::v2::DeviceId id = m_model->itemAt(row).id;
    for (int i = 0; i < m_configs.size(); ++i) {
        if (m_configs.at(i).id == id) {
            m_configs.removeAt(i);
            break;
        }
    }
    m_model->removeDevice(row);
    saveDevices();

    m_statusLabel->setText(tr("已删除设备"));
    onSelectionChanged();  // 更新按钮可用性
}

void DevicePage::onConnect()
{
    const int row = currentRow();
    const DeviceConfig *cfg = configOfRow(row);
    if (!cfg) {
        m_statusLabel->setText(tr("请先在列表中选择要连接的设备"));
        return;
    }

    // 下发连接前把状态置为"连接中"（UI 即时反馈，异步结果随后回写）
    updateModelState(row, datascope::domain::v2::DeviceState::Connecting);
    m_statusLabel->setText(tr("正在连接 %1（%2:%3）...")
                               .arg(cfg->name).arg(cfg->host).arg(cfg->port));

    // 数据总线异步连接 + 启动采集（连接成功才真正采集）
    m_dataService->connectTo(cfg->host, cfg->port);
    m_dataService->startAcquisition();
}

void DevicePage::onDisconnect()
{
    const int row = currentRow();
    if (row < 0)
        return;

    // 停止采集 + 断开，状态回写 Disconnected（DataService 的 disconnected 信号也会触发）
    m_dataService->stopAcquisition();
    m_dataService->disconnectFromDevice();
    updateModelState(row, datascope::domain::v2::DeviceState::Disconnected);
    m_statusLabel->setText(tr("已断开连接"));
}

void DevicePage::onSelectionChanged()
{
    // 选中行决定"删除/连接/断开"是否可用；无选中一律禁用
    const bool hasSelection = currentRow() >= 0;
    m_removeBtn->setEnabled(hasSelection);
    m_connectBtn->setEnabled(hasSelection);
    m_disconnectBtn->setEnabled(hasSelection);
}

// ---------------------------------------------------------------------------
// DataService 信号回写（连接结果 → 状态列 + 提示）
// ---------------------------------------------------------------------------

void DevicePage::onServiceConnected()
{
    const int row = currentRow();
    if (row < 0)
        return;
    updateModelState(row, datascope::domain::v2::DeviceState::Online);
    m_statusLabel->setText(tr("设备已连接"));
}

void DevicePage::onServiceDisconnected()
{
    const int row = currentRow();
    if (row < 0)
        return;
    updateModelState(row, datascope::domain::v2::DeviceState::Disconnected);
}

void DevicePage::onServiceError(const QString &message)
{
    // 连接失败：状态列 → 出错（可重连），提示区红字信息
    const int row = currentRow();
    if (row >= 0)
        updateModelState(row, datascope::domain::v2::DeviceState::Error);
    m_statusLabel->setText(tr("连接失败：%1").arg(message));
}

// ---------------------------------------------------------------------------
// 持久化（ConfigManager：INI 文件中的 JSON 数组）
// ---------------------------------------------------------------------------

void DevicePage::loadDevices()
{
    // 读配置键 → 解析 JSON 数组 → 逐个重建设备（展示 + 完整配置）
    const QJsonDocument doc = QJsonDocument::fromJson(
        datascope::infrastructure::ConfigManager::instance()
            .stringValue(kDevicesKey).toUtf8());
    if (!doc.isArray())
        return;  // 空配置或损坏：静默返回（首次启动正常路径）

    const QJsonArray array = doc.array();
    for (const QJsonValue &v : array) {
        const QJsonObject obj = v.toObject();

        DeviceConfig cfg;
        cfg.id        = datascope::domain::v2::DeviceId(obj.value(QStringLiteral("id")).toString());
        cfg.name      = obj.value(QStringLiteral("name")).toString();
        cfg.host      = obj.value(QStringLiteral("host")).toString();
        cfg.port      = static_cast<quint16>(obj.value(QStringLiteral("port")).toInt(kDefaultPort));
        cfg.timeoutMs = obj.value(QStringLiteral("timeout")).toInt(kDefaultTimeoutMs);
        cfg.channels  = obj.value(QStringLiteral("channels")).toInt(kDefaultChannels);

        if (!cfg.id.isValid() || cfg.name.isEmpty())
            continue;  // 跳过损坏条目，不阻塞其余设备加载

        datascope::ui::models::DeviceListModel::DeviceItem item;
        item.id          = cfg.id;
        item.name        = cfg.name;
        item.connType    = datascope::domain::v2::ConnectionType::Tcp;
        item.state       = datascope::domain::v2::DeviceState::Disconnected;
        item.channelCount = cfg.channels;
        item.address     = QStringLiteral("%1:%2").arg(cfg.host).arg(cfg.port);
        m_model->appendDevice(item);
        m_configs.append(cfg);
    }
}

void DevicePage::saveDevices()
{
    // 把完整配置序列化为 JSON 数组写入 ConfigManager（sync 立即落盘）
    QJsonArray array;
    for (const DeviceConfig &cfg : m_configs) {
        QJsonObject obj;
        obj.insert(QStringLiteral("id"), cfg.id.value());
        obj.insert(QStringLiteral("name"), cfg.name);
        obj.insert(QStringLiteral("host"), cfg.host);
        obj.insert(QStringLiteral("port"), cfg.port);
        obj.insert(QStringLiteral("timeout"), cfg.timeoutMs);
        obj.insert(QStringLiteral("channels"), cfg.channels);
        array.append(obj);
    }

    datascope::infrastructure::ConfigManager::instance()
        .setValue(kDevicesKey, QJsonDocument(array).toJson(QJsonDocument::Compact));
    datascope::infrastructure::ConfigManager::instance().sync();
}

// ---------------------------------------------------------------------------
// 工具方法
// ---------------------------------------------------------------------------

int DevicePage::currentRow() const
{
    const QModelIndexList selected = m_tableView->selectionModel()->selectedRows();
    if (selected.isEmpty())
        return -1;
    return selected.first().row();
}

const DevicePage::DeviceConfig *DevicePage::configOfRow(int row) const
{
    if (row < 0)
        return nullptr;

    // 行 → id（经 Model）→ 完整配置（本页 m_configs）
    const datascope::domain::v2::DeviceId id = m_model->itemAt(row).id;
    for (int i = 0; i < m_configs.size(); ++i) {
        if (m_configs.at(i).id == id)
            return &m_configs.at(i);
    }
    return nullptr;
}

void DevicePage::updateModelState(int row, datascope::domain::v2::DeviceState state)
{
    // 越界保护交给 Model（内部有 row 校验）；状态列由 dataChanged 自动重绘
    m_model->setDeviceState(row, state);
}

} // namespace ui
} // namespace datascope
