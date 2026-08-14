/**
 * @file recordmanager.h
 * @brief 数据记录与回放管理（P15）
 *
 * 职责：
 *   - 记录：把实时采集的 DataPoint 流落盘为 CSV（每行一个采样点）；
 *   - 回放：读回 CSV → 按时间戳重组为"帧" → 定时逐帧发送 replayData 信号，
 *     驱动监控页（MonitorPage::onDataUpdated）重放波形。
 *
 * 设计要点（对照 WPF）：
 *   - 记录 ≈ C# 里"后台订阅数据事件 → 写 StreamWriter"；本类在主线程被
 *     DataService::dataUpdated 调用（连接已在 MainWindow 建立），内部判定
 *     m_recording 决定是否落盘；
 *   - 回放 ≈ 读文件 → 内存缓冲 → QTimer 节拍重放，进度由 UI 展示；
 *   - CSV 是工业数据交换的通用格式，Excel/WPS 可直接打开分析。
 */

#pragma once

#include <QObject>
#include <QString>
#include <QVector>
#include <QFile>

#include "domain/models.h"   // DataPoint

class QTimer;

namespace datascope {
namespace services {

/**
 * @class RecordManager
 * @brief 采集数据记录（CSV 落盘）与历史回放（定时逐帧重放）
 */
class RecordManager : public QObject
{
    Q_OBJECT

public:
    /**
     * @brief 构造：创建回放定时器
     * @param parent 父对象
     */
    explicit RecordManager(QObject *parent = nullptr);

    // ---- 记录 ----

    /**
     * @brief 开始记录：打开 CSV 文件并写表头
     * @param filePath 目标文件路径（如 D:/data/record1.csv）
     * @return 是否成功（文件可写）
     */
    bool startRecording(const QString &filePath);

    /**
     * @brief 停止记录：flush 并关闭文件
     */
    void stopRecording();

    /**
     * @brief 是否正在记录
     */
    bool isRecording() const;

    /**
     * @brief 追加一批采样点（连接 DataService::dataUpdated，未记录时忽略）
     * @param points 一批 DataPoint（通常 4 通道）
     */
    void appendData(const QVector<domain::DataPoint> &points);

    // ---- 回放 ----

    /**
     * @brief 加载回放文件（读 CSV → 按时间戳重组成帧）
     * @param filePath CSV 文件路径
     * @return 是否成功；成功后可用 startReplay() 开始回放
     */
    bool loadReplay(const QString &filePath);

    /**
     * @brief 开始回放：按帧定时发送 replayData 信号
     * @param intervalMs 每帧间隔毫秒（默认 100ms，即 10 帧/秒）
     */
    void startReplay(int intervalMs = 100);

    /**
     * @brief 停止回放
     */
    void stopReplay();

    /**
     * @brief 是否正在回放
     */
    bool isReplaying() const;

    /**
     * @brief 已加载的回放总帧数（进度条上限）
     */
    int replayFrameCount() const;

    /**
     * @brief 当前回放进度（已发送帧数）
     */
    int replayProgress() const;

signals:
    /** @brief 回放发出一帧数据（驱动 MonitorPage::onDataUpdated） */
    void replayData(const QVector<datascope::domain::DataPoint> &points);

    /** @brief 回放结束（帧发完） */
    void replayFinished();

    /** @brief 回放进度变化（已发送帧数，驱动 UI 进度条） */
    void progressChanged(int current);

    /** @brief 错误提示（文件无法打开/解析失败等） */
    void errorOccurred(const QString &message);

private slots:
    /** @brief 回放定时器节拍：发下一帧 */
    void onReplayTick();

private:
    // ---- 记录状态 ----
    QFile m_recordFile;            // CSV 输出文件（Open 中 = 正在记录）
    bool  m_recording = false;     // 记录开关

    // ---- 回放状态 ----
    QVector<QVector<domain::DataPoint>> m_replayFrames;   // 已加载的回放帧（每帧=一个时间戳的全部通道）
    int   m_replayIndex = 0;       // 下一帧要发的下标
    QTimer *m_replayTimer = nullptr;   // 回放节拍定时器（单次驱动，槽内决定是否续走）
};

} // namespace services
} // namespace datascope
