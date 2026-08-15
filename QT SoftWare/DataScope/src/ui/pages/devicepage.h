/**
 * @file devicepage.h
 * @brief 设备管理页（V2 重构：真实设备管理，替代 P5 纯占位骨架）
 *
 * ────────────────────────────────────────────────────────────
 * 为什么重构（对应审查 P0-2：设备管理页是纯空壳）
 * ────────────────────────────────────────────────────────────
 * V1 的 DevicePage 只有标题 + 一段"将来会做什么"的占位文字，没有任何真实功能。
 * 真实工业监控软件的设备管理页必须能做三件事：
 *   1. 配置设备 —— 名称 / 地址 / 端口 / 超时 / 通道数，且持久化（重启不丢失）；
 *   2. 管理设备 —— 添加 / 删除，列表实时展示状态；
 *   3. 连接设备 —— 选中一台设备下发连接，连接结果回写状态列。
 *
 * 本页数据流（对应 WPF 的 DataGrid + ObservableCollection）：
 *   DeviceListModel（表格模型，已实现并单测）
 *     └─ QTableView 展示
 *     └─ ConfigManager 持久化（INI 中存 JSON 数组，重启加载）
 *   DataService（数据总线，P12）—— 连接 / 断开 / 采集的驱动源
 *     └─ connected/disconnected/connectionError 信号回写状态列
 *
 * ── 设计要点 ──
 *   - 依赖注入：构造时传入 DataService*（数据总线），不 new、不静态访问
 *     —— 便于测试与替换（对应 WPF 构造函数注入 ViewModel/Service）；
 *   - Model/View 分离：页面只做"用户动作 → Model/Service 调用"，状态变化
 *     由 Model 的 dataChanged 通知 View 自动刷新，无手动 setText；
 *   - 持久化真实：添加/删除设备即写 ConfigManager，启动加载 —— 不是演示数据。
 */

#pragma once

#include <QWidget>
#include <QVector>

#include "domain/domainmodel_v2.h"   // DeviceState（状态回写用）

class QTableView;
class QLineEdit;
class QSpinBox;
class QPushButton;
class QLabel;

namespace datascope {
namespace services {
class DataService;       // 数据总线（连接/断开/采集驱动，前向声明避免重 include）
} // namespace services

namespace ui {
namespace models {
class DeviceListModel;   // V2 设备表格模型（已实现 + 单测）
} // namespace models

/**
 * @class DevicePage
 * @brief 设备管理页（V2）：设备配置 + 列表 + 连接控制
 *
 * 布局（左表右表单，工业监控软件常见布局）：
 *   左侧  QTableView（设备列表：名称/类型/状态/通道/地址）
 *   右侧  设备配置表单（名称/主机/端口/超时/通道数）+ 操作按钮
 */
class DevicePage : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造设备管理页
     * @param dataService 数据总线（非空；连接/断开都通过它驱动）
     * @param parent 父控件（主窗口 QTabWidget 页签容器）
     */
    explicit DevicePage(datascope::services::DataService *dataService,
                        QWidget *parent = nullptr);

    // 禁止拷贝：QObject 派生类不可复制
    Q_DISABLE_COPY(DevicePage)

private slots:
    // ---- 用户动作 ----
    void onAddDevice();           // 添加设备（表单校验 → 入模型 + 持久化）
    void onRemoveDevice();        // 删除选中设备
    void onConnect();             // 连接选中设备
    void onDisconnect();          // 断开选中设备
    void onSelectionChanged();    // 列表选中行变化：同步按钮可用性

    // ---- DataService 信号回写 ----
    void onServiceConnected();    // 连接成功：选中行状态 → Online
    void onServiceDisconnected(); // 断开：选中行状态 → Disconnected
    void onServiceError(const QString &message);  // 链路错误：状态 → Error + 提示

private:
    // ---- UI 构建 ----
    void buildUi();               // 左右分栏：左表右表单
    void buildDeviceTable();      // 左侧设备列表（QTableView + DeviceListModel）
    QWidget *buildForm();         // 右侧配置表单 + 操作按钮（返回面板控件）

    // ---- 持久化 ----
    void loadDevices();           // 启动：从 ConfigManager 读取设备列表（JSON）
    void saveDevices();           // 变更后：把模型写入 ConfigManager（JSON 数组）

    /**
     * @brief 一台设备的完整配置（页面私有数据源）
     * @note DeviceListModel 只保存"展示快照"（address 是 host:port 拼出的文本），
     *       连接动作需要 host/port 分开 —— 所以完整配置由本页维护，两者用 id 关联。
     * @note 定义在私有成员声明之前：C++ 要求成员函数签名中出现嵌套类型时，
     *       该类型必须先于成员函数声明（complete-class context 规则外）。
     */
    struct DeviceConfig {
        datascope::domain::v2::DeviceId id;   ///< 设备唯一标识（与 Model 行关联）
        QString  name;                        ///< 设备名称
        QString  host;                        ///< 主机地址
        quint16  port = 0;                    ///< 端口
        int      timeoutMs = 5000;            ///< 连接超时（毫秒）
        int      channels = 0;                ///< 通道数
    };

    // ---- 工具 ----
    int currentRow() const;                           // 当前选中行（无选中返回 -1）
    const DeviceConfig *configOfRow(int row) const;   // 行 → 完整配置（无则 nullptr）
    void updateModelState(int row, datascope::domain::v2::DeviceState state);

private:
    datascope::services::DataService *m_dataService = nullptr;  ///< 数据总线（注入）
    datascope::ui::models::DeviceListModel *m_model  = nullptr; ///< 设备表格模型
    QVector<DeviceConfig> m_configs;   ///< 设备完整配置列表（本页唯一 owner）

    // ---- 控件 ----
    QTableView *m_tableView  = nullptr;   ///< 设备列表
    QLineEdit  *m_nameEdit   = nullptr;   ///< 设备名称输入
    QLineEdit  *m_hostEdit   = nullptr;   ///< 主机地址输入
    QSpinBox   *m_portSpin   = nullptr;   ///< 端口输入
    QSpinBox   *m_timeoutSpin = nullptr;  ///< 连接超时输入（毫秒）
    QSpinBox   *m_channelSpin = nullptr;  ///< 通道数输入
    QPushButton *m_addBtn    = nullptr;   ///< 添加设备
    QPushButton *m_removeBtn = nullptr;   ///< 删除设备
    QPushButton *m_connectBtn = nullptr;  ///< 连接设备
    QPushButton *m_disconnectBtn = nullptr; ///< 断开设备
    QLabel      *m_statusLabel = nullptr; ///< 本页状态提示（校验/错误/连接结果）
};

} // namespace ui
} // namespace datascope
