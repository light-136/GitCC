/**
 * @file recordpage.h
 * @brief 数据记录与回放页（P15：记录/回放功能填充）
 *
 * P15 职责：
 *   - 实时记录：把当前采集数据流落盘为 CSV（通过 RecordManager）；
 *   - 历史回放：加载 CSV → 定时逐帧重放，驱动监控页波形重绘；
 *   - 记录管理器由 MainWindow 创建并注入（setRecordManager），本页只管 UI 与按钮编排，
 *     不感知数据来源（符合"页面只管呈现、业务下沉 services"的分层）。
 *
 * 对应 WPF 锚点：
 *   - 类似"数据导出 + 播放器控件"组合；回放进度用 QProgressBar 展示，
 *     与实时监控（P14）共用同一波形绘制组件（MonitorPage::onDataUpdated）。
 */

#pragma once

#include <QWidget>

class QLabel;
class QPushButton;
class QProgressBar;

namespace datascope {
namespace services {
class RecordManager;   // 记录/回放管理器（services 层）
} // namespace services

namespace ui {

/**
 * @class RecordPage
 * @brief 数据记录与回放页签（记录 CSV + 历史波形回放）
 */
class RecordPage : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造数据记录与回放页
     * @param parent 父控件（主窗口的 QTabWidget 会以本页为页签容器）
     */
    explicit RecordPage(QWidget *parent = nullptr);

    // 禁止拷贝：QObject 派生类不可复制
    Q_DISABLE_COPY(RecordPage)

    /**
     * @brief 注入记录管理器（由 MainWindow 创建并接线数据源后调用）
     * @param manager 非空指针；本页所有按钮动作都走它
     */
    void setRecordManager(datascope::services::RecordManager *manager);

private slots:
    void onStartRecordClicked();      // 开始记录：选保存路径 → manager 开始落盘
    void onStopRecordClicked();       // 停止记录
    void onLoadReplayClicked();       // 选择并加载回放文件
    void onStartReplayClicked();      // 开始回放
    void onStopReplayClicked();       // 停止回放
    void onReplayProgress(int current);  // 回放进度 → 进度条
    void onReplayFinished();          // 回放结束 → 按钮复位

private:
    /**
     * @brief 构建本页 UI（记录组 + 回放组两个功能区块）
     */
    void buildUi();

    datascope::services::RecordManager *m_manager = nullptr;   // 记录/回放管理器

    // ---- 控件句柄（按钮状态切换用）----
    QLabel       *m_recordStateLabel = nullptr;   // 记录状态（未记录/正在记录/路径）
    QPushButton  *m_startRecordBtn = nullptr;     // 开始记录
    QPushButton  *m_stopRecordBtn  = nullptr;     // 停止记录
    QLabel       *m_replayFileLabel = nullptr;    // 回放文件信息（文件名/帧数）
    QProgressBar *m_replayProgress  = nullptr;    // 回放进度条
    QPushButton  *m_loadReplayBtn   = nullptr;    // 加载回放文件
    QPushButton  *m_startReplayBtn  = nullptr;    // 开始回放
    QPushButton  *m_stopReplayBtn   = nullptr;    // 停止回放
};

} // namespace ui
} // namespace datascope
