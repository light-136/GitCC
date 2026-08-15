/**
 * @file monitorpage.h
 * @brief 监控总览页（P14 完整版 + V2-执行③ 通道元数据数据驱动）
 *
 * P14 设计意图：
 *   - 本页是主窗口 5 个页签之一（数据采集系统的"仪表盘"入口）；
 *   - 每通道一张卡片：LED 状态灯 + 数值标签 + 实时曲线 + 仪表盘；
 *   - 演示数据源（QTimer + 正弦波）用于早期看效果，P12 起由真实链路替代
 *     （主窗口 setDemoRunning(false) 显式停用）。
 *
 * V2-执行③ 改造（通道元数据数据驱动）：
 *   - 工程问题：V1 的通道名/单位/量程是写死的常量数组（温度/压力/流量/振动），
 *     设备通道数或量程一变，UI 无法感知——真实工业软件必须"设备是唯一事实来源"；
 *   - 链路：连接建立 → Worker 发通道配置查询(0x03/0x01) → 设备应答(0x83) →
 *     Worker 解码 → DataService::channelConfigReceived → 本页 onChannelConfigReceived
 *     → 更新卡片标题/单位/量程 + 重建报警规则；
 *   - 卡片数量随配置变化：m_channelArea 容器可整体重建（增/减通道卡片）。
 *
 * 数据源可替换设计（教学点，对应 WPF）：
 *   - 演示源（QTimer）与真实源（DataService）都汇聚到同一个槽 onDataUpdated：
 *     监控页只消费"QVector<DataPoint>"，不关心数据从哪来——
 *     类似 WPF 里 ViewModel 暴露同一接口，数据源换成什么都不影响 View。
 *
 * 对应 WPF 的锚点：
 *   - 类似 WPF 中 TabControl 的一个 TabItem，宿主是主窗口的 QTabWidget；
 *   - 卡片里的曲线/仪表/LED 是自绘控件（P14 三件套）的消费者。
 */

#pragma once

#include <QVector>
#include <QWidget>

#include "protocol/channelconfigcodec.h"   // V2-执行③：ChannelConfigInfo（配置信号参数）

// 前向声明：减少头文件依赖，加快编译
// 注意：QTimer / QLabel 是 Qt 全局类，必须在全局命名空间声明；
// 而自绘控件是工程类（datascope::ui），在命名空间内声明。
class QTimer;      // 演示数据源定时器（P12 后改为真实 DataService 驱动）
class QLabel;
class QListView;   // 报警中心列表（V2 报警中心接线）
class QVBoxLayout;

namespace datascope {
namespace domain {
struct DataPoint;
}
} // namespace datascope

namespace datascope {
namespace services {
class AlarmEngine;   // V2 报警引擎（服务层：评估规则产生报警事件）
} // namespace services

namespace ui {
namespace models {
class AlarmEventModel;  // V2 报警事件列表模型（报警中心数据源）
} // namespace models

class LineChartWidget;
class GaugeWidget;
class LedIndicator;

/**
 * @class MonitorPage
 * @brief 监控总览页签（通道卡片曲线仪表 + 设备配置数据驱动）
 */
class MonitorPage : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造监控总览页
     * @param parent 父控件（主窗口的 QTabWidget 会以本页为页签容器）
     */
    explicit MonitorPage(QWidget *parent = nullptr);

    // 禁止拷贝：QObject 派生类不可复制（对应 C# sealed + 无深拷贝语义）
    Q_DISABLE_COPY(MonitorPage)

    /**
     * @brief 演示数据源是否运行中
     * @return true = 内部 QTimer 在驱动演示正弦波
     */
    bool isDemoRunning() const;

    /**
     * @brief 启动/停止演示数据源
     * @param enabled true = 启动演示（P11/P12 未接入前用于看效果）
     */
    void setDemoRunning(bool enabled);

public slots:
    /**
     * @brief 采集数据入口（P12 真实数据流接入点）
     * @param points 一次采样的一组数据点（每个通道一个，channelIndex 定位通道）
     * @note 演示 QTimer 也调本槽；未来 connect(DataService::dataUpdated → 本槽)
     */
    void onDataUpdated(const QVector<domain::DataPoint> &points);

    /**
     * @brief 通道配置上报入口（V2-执行③：设备真实配置 → 数据驱动渲染）
     * @param channels 设备上报的通道配置（名称/单位/量程）
     * @note 通道数变化时重建卡片区；仅数量相同时逐卡片更新元数据。
     */
    void onChannelConfigReceived(
        const QVector<datascope::protocol::ChannelConfigInfo> &channels);

private:
    /** @brief 单个通道的控件组（曲线 + 仪表 + LED + 名称 + 数值标签） */
    struct ChannelUi {
        LineChartWidget *chart = nullptr;      ///< 实时曲线
        GaugeWidget     *gauge = nullptr;      ///< 仪表盘
        LedIndicator    *led   = nullptr;      ///< LED 状态灯（超限报警）
        QLabel          *nameLabel = nullptr;  ///< 通道名标签（V2-执行③：配置驱动）
        QLabel          *valueLabel = nullptr; ///< 当前值文本（含单位）
    };

    /**
     * @brief 构建本页 UI：标题 + 通道卡片区 + 底部报警中心
     * @note 卡片数量由 m_channelConfigs 决定（数据驱动，不写死 4 张）
     */
    void buildUi();

    /**
     * @brief 构建底部报警中心（标题 + QListView + 确认全部按钮）
     * @note 报警中心数据流：onDataUpdated → AlarmEngine.evaluate →
     *       ruleTriggered/ruleRecovered → AlarmEventModel.appendEvent → QListView 展示
     */
    QWidget *buildAlarmCenter();

    /**
     * @brief 依据当前通道量程派生报警规则并注入 AlarmEngine（先清空再重建）
     * @note 阈值来自通道配置（rangeMax × kAlarmRatio），配置变化时重调本函数
     */
    void setupAlarmRules();

    /**
     * @brief 构建单张通道卡片（第 index 通道的曲线/仪表/LED/名称/数值）
     * @param index 通道序号（0 基，对应 m_channelConfigs[index]）
     * @return 新卡片 QFrame 的指针（由 m_channelArea 接管所有权）
     */
    QWidget *buildChannelCard(int index);

    /**
     * @brief 按 m_channelConfigs 重建整个通道卡片区（通道数变化时调用）
     * @note 清空容器内旧卡片 → 重新 buildChannelCard 填充
     */
    void rebuildChannelArea();

    /**
     * @brief 更新单张卡片的元数据（名称/单位/量程）——通道数不变时调用
     * @param index 通道序号
     */
    void applyConfigToCard(int index);

    /**
     * @brief 初始化默认通道配置（4 通道，与模拟设备默认对齐）
     * @note 设备应答配置帧前的兜底值；收到真实配置后整体替换
     */
    void buildDefaultChannelConfig();

    /**
     * @brief 演示数据源：每 100ms 产生 4 路正弦波并走 onDataUpdated
     * @note P12 接真实数据流后已停用（setDemoRunning(false)），代码保留作教学对照
     */
    void onDemoTick();

    // ---- 演示数据源参数（4 路正弦波；已停用，教学保留）----
    QTimer *m_demoTimer   = nullptr;        ///< 演示定时器（100ms）
    double  m_phase       = 0.0;            ///< 当前相位（每 tick 递增）
    bool    m_demoRunning = false;          ///< 演示源运行标志

    /** @brief 演示源数组尺寸（P14 教学代码保留；真实通道数以配置为准） */
    static constexpr int kChannelCount = 4;

    /** @brief 第 i 通道的演示参数：中心值 / 幅度 / 角频率 */
    double m_center[kChannelCount]   = { 50.0,  5.0, 25.0, 2.5 };
    double m_amplitude[kChannelCount] = { 30.0,  3.0, 15.0, 1.5 };
    double m_frequency[kChannelCount] = { 0.5,  0.3,  0.4, 1.0 };

    /** @brief 报警阈值比例：值 > 量程上限 × 此比例 触发报警（LED 红 + 报警事件） */
    static constexpr double kAlarmRatio = 0.85;

    QVector<ChannelUi> m_channels;          ///< 每通道控件组（数量随配置变化）
    QVector<datascope::protocol::ChannelConfigInfo> m_channelConfigs;  ///< 当前生效通道配置
    QVBoxLayout *m_channelArea = nullptr;   ///< 通道卡片容器（可整体重建）

    // ---- V2 报警中心（审查 P1"无报警区域"落地修复）----
    datascope::services::AlarmEngine *m_alarmEngine = nullptr;   ///< 报警引擎（规则评估）
    datascope::ui::models::AlarmEventModel *m_alarmModel = nullptr; ///< 报警事件列表模型
    QListView  *m_alarmList = nullptr;   ///< 报警中心列表视图
    quint64    m_recoverId = 1;          ///< 恢复事件的 id（与报警事件 id 区分）
};

} // namespace ui
} // namespace datascope

// V2-执行③：通道配置列表作为跨线程信号参数，注册进元类型系统
Q_DECLARE_METATYPE(QVector<datascope::protocol::ChannelConfigInfo>)
