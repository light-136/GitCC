/**
 * @file monitorpage.h
 * @brief 监控总览页（P14 完整版：4 通道实时曲线仪表 + 演示数据源）
 *
 * P14 设计意图：
 *   - 本页是主窗口 5 个页签之一（数据采集系统的"仪表盘"入口）；
 *   - P5 只搭骨架；P14 把占位替换为"每通道一张卡片：LED 状态灯 +
 *     数值标签 + 实时曲线 + 仪表盘"的完整监控布局；
 *   - 演示数据源（QTimer + 正弦波）让监控页立刻"动起来"；
 *     真实数据流（P12 DataService 的生产者-消费者链路）接入时，
 *     把 QTimer 演示逻辑替换为 connect(DataService::dataUpdated →
 *     onDataUpdated) 即可，入口统一走 onDataUpdated(QVector<DataPoint>)。
 *
 * 数据源可替换设计（教学点，对应 WPF）：
 *   - 演示源（QTimer）与真实源（DataService）都汇聚到同一个槽 onDataUpdated：
 *     监控页只消费"QVector<DataPoint>"，不关心数据从哪来——
 *     类似 WPF 里 ViewModel 暴露同一接口，数据源换成什么都不影响 View。
 *   - DataPoint 来自领域层（datascope::domain），UI 依赖领域模型是
 *     分层架构允许的方向（UI → domain）。
 *
 * 对应 WPF 的锚点：
 *   - 类似 WPF 中 TabControl 的一个 TabItem，宿主是主窗口的 QTabWidget；
 *   - 卡片里的曲线/仪表/LED 是自绘控件（P14 三件套）的消费者。
 */

#pragma once

#include <QVector>
#include <QWidget>

// 前向声明：减少头文件依赖，加快编译
// 注意：QTimer / QLabel 是 Qt 全局类，必须在全局命名空间声明；
// 而自绘控件是工程类（datascope::ui），在命名空间内声明。
class QTimer;      // 演示数据源定时器（P12 后改为真实 DataService 驱动）
class QLabel;
class QListView;   // 报警中心列表（V2 报警中心接线）

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
 * @brief 监控总览页签（4 通道曲线仪表 + 演示数据源）
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

private:
    /** @brief 单个通道的控件组（曲线 + 仪表 + LED + 数值标签） */
    struct ChannelUi {
        LineChartWidget *chart = nullptr;   ///< 实时曲线
        GaugeWidget     *gauge = nullptr;   ///< 仪表盘
        LedIndicator    *led   = nullptr;   ///< LED 状态灯（超限报警）
        QLabel          *valueLabel = nullptr; ///< 当前值文本（含单位）
    };

    /**
     * @brief 构建本页 UI：标题 + 4 张通道卡片 + 底部报警中心
     * @note 布局、父子层级、objectName 都在这里集中构建，方便阅读
     */
    void buildUi();

    /**
     * @brief 构建底部报警中心（标题 + QListView + 确认全部按钮）
     * @note 报警中心数据流：onDataUpdated → AlarmEngine.evaluate →
     *       ruleTriggered/ruleRecovered → AlarmEventModel.appendEvent → QListView 展示
     */
    QWidget *buildAlarmCenter();

    /**
     * @brief 依据当前通道量程派生报警规则并注入 AlarmEngine
     * @note 规则源：每通道"超过量程上限×报警比例"触发（阈值来自通道配置，
     *       后续通道配置改为模型驱动时，规则随之由真实配置派生）
     */
    void setupAlarmRules();

    /**
     * @brief 构建单张通道卡片（第 i 通道的曲线/仪表/LED/数值）
     * @param index 通道序号（0-3）
     * @return 新卡片 QFrame 的指针（由本页布局接管所有权）
     */
    QWidget *buildChannelCard(int index);

    /**
     * @brief 演示数据源：每 100ms 产生 4 路正弦波并走 onDataUpdated
     * @note 教学点：QTimer 定时回调生成数据 → 汇聚到统一数据入口。
     *       P12 接真实数据流后，本函数改为由 DataService 驱动（或停用）。
     */
    void onDemoTick();

    // ---- 演示数据源参数（4 路正弦波：温度/压力/流量/振动）----
    QTimer *m_demoTimer   = nullptr;        ///< 演示定时器（100ms）
    double  m_phase       = 0.0;            ///< 当前相位（每 tick 递增）
    bool    m_demoRunning = false;          ///< 演示源运行标志

    // ---- 通道配置（演示阶段用；P12 起由真实设备配置替代）----
    static constexpr int kChannelCount = 4; ///< 通道数（与模拟设备 4 通道一致）

    /** @brief 第 i 通道的演示参数：中心值 / 幅度 / 角频率 / 报警上限（量程 0..max） */
    double m_center[kChannelCount]   = { 50.0,  5.0, 25.0, 2.5 };
    double m_amplitude[kChannelCount] = { 30.0,  3.0, 15.0, 1.5 };
    double m_frequency[kChannelCount] = { 0.5,  0.3,  0.4, 1.0 };
    double m_max[kChannelCount]       = { 100.0, 10.0, 50.0, 5.0 };
    double m_alarmRatio[kChannelCount]= { 0.85, 0.85, 0.85, 0.85 }; ///< 报警阈值（相对量程上限）

    ChannelUi m_channels[kChannelCount];    ///< 每通道控件组

    // ---- V2 报警中心（审查 P1"无报警区域"落地修复）----
    datascope::services::AlarmEngine *m_alarmEngine = nullptr;   ///< 报警引擎（规则评估）
    datascope::ui::models::AlarmEventModel *m_alarmModel = nullptr; ///< 报警事件列表模型
    QListView  *m_alarmList = nullptr;   ///< 报警中心列表视图
    quint64    m_recoverId = 1;          ///< 恢复事件的 id（与报警事件 id 区分）
};

} // namespace ui
} // namespace datascope
