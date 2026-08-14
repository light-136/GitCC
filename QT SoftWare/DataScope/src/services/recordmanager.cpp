/**
 * @file recordmanager.cpp
 * @brief 数据记录与回放管理实现（P15）
 *
 * CSV 格式约定（每行一个采样点）：
 *   timestampMs,channelIndex,value
 *   如：1734168000123,0,42.5
 *   表头固定为 "timestampMs,channelIndex,value"，回放时跳过表头、按时间戳分组。
 *
 * 回放重组逻辑：
 *   同一时间戳下的多个通道点视为"一帧"（对应采集时每帧 4 通道）。
 *   读入后按时间戳升序排成帧序列，QTimer 每 tick 发一帧。
 */

#include "services/recordmanager.h"

#include <QTextStream>
#include <QDateTime>
#include <QTimer>
#include <QDebug>

namespace datascope {
namespace services {

namespace {
// CSV 表头：timestampMs(毫秒时间戳),channelIndex(通道号),value(采样值)
const char *kCsvHeader = "timestampMs,channelIndex,value\n";
} // namespace

RecordManager::RecordManager(QObject *parent)
    : QObject(parent)
{
    m_replayTimer = new QTimer(this);
    m_replayTimer->setInterval(100);   // 默认 100ms/帧（10 帧/秒）
    connect(m_replayTimer, &QTimer::timeout, this, &RecordManager::onReplayTick);
}

// ---------------------------------------------------------------------------
// 记录
// ---------------------------------------------------------------------------
bool RecordManager::startRecording(const QString &filePath)
{
    stopRecording();   // 若正在记录：先关旧文件（一个管理器同时只写一个文件）

    m_recordFile.setFileName(filePath);
    if (!m_recordFile.open(QIODevice::WriteOnly | QIODevice::Text | QIODevice::Truncate)) {
        emit errorOccurred(QStringLiteral("无法打开记录文件: %1").arg(filePath));
        return false;
    }
    m_recordFile.write(kCsvHeader);   // 写表头，Excel 打开即可识别列名
    m_recording = true;
    return true;
}

void RecordManager::stopRecording()
{
    if (m_recordFile.isOpen()) {
        m_recordFile.flush();
        m_recordFile.close();
    }
    m_recording = false;
}

bool RecordManager::isRecording() const
{
    return m_recording;
}

void RecordManager::appendData(const QVector<domain::DataPoint> &points)
{
    // 记录开关关闭时静默忽略（MainWindow 常驻连接本信号，这里做内部过滤）
    if (!m_recording || !m_recordFile.isOpen()) {
        return;
    }
    // QTextStream 按行写：时间戳,通道号,数值（默认精度足够展示，且文件小巧）
    QTextStream out(&m_recordFile);
    for (const domain::DataPoint &p : points) {
        out << p.timestamp.toMSecsSinceEpoch()
            << ',' << p.channelIndex
            << ',' << p.value
            << '\n';
    }
}

// ---------------------------------------------------------------------------
// 回放
// ---------------------------------------------------------------------------
bool RecordManager::loadReplay(const QString &filePath)
{
    stopReplay();   // 加载新文件前先停旧回放
    m_replayFrames.clear();

    QFile in(filePath);
    if (!in.open(QIODevice::ReadOnly | QIODevice::Text)) {
        emit errorOccurred(QStringLiteral("无法打开回放文件: %1").arg(filePath));
        return false;
    }

    QTextStream reader(&in);
    // 哈希：时间戳 → 该时刻的全部通道点（可能 1~4 个，取决于记录时采集帧）
    QHash<qint64, QVector<domain::DataPoint>> framesByTime;
    if (!reader.atEnd()) {
        reader.readLine();   // 跳过表头
    }
    while (!reader.atEnd()) {
        const QString line = reader.readLine().trimmed();
        if (line.isEmpty()) {
            continue;   // 空行（文件末尾换行）跳过
        }
        const QStringList parts = line.split(',');
        if (parts.size() < 3) {
            continue;   // 畸形行：本行丢弃，不中断整个文件
        }
        domain::DataPoint dp;
        dp.timestamp    = QDateTime::fromMSecsSinceEpoch(parts.at(0).toLongLong());
        dp.channelIndex = parts.at(1).toInt();
        dp.value        = parts.at(2).toDouble();
        framesByTime[dp.timestamp.toMSecsSinceEpoch()].append(dp);
    }

    // 按时间戳升序排成帧序列（QHash 无序，必须显式排序保证回放顺序）
    const QList<qint64> keys = framesByTime.keys();
    m_replayFrames.clear();
    m_replayFrames.reserve(keys.size());
    QList<qint64> sortedKeys = keys;
    std::sort(sortedKeys.begin(), sortedKeys.end());
    for (qint64 ts : sortedKeys) {
        m_replayFrames.append(framesByTime.value(ts));
    }

    m_replayIndex = 0;
    return true;
}

void RecordManager::startReplay(int intervalMs)
{
    if (m_replayFrames.isEmpty()) {
        emit errorOccurred(QStringLiteral("请先加载回放文件"));
        return;
    }
    stopReplay();   // 重置：从第一帧开始
    m_replayIndex = 0;
    m_replayTimer->setInterval(intervalMs);
    m_replayTimer->start();
}

void RecordManager::stopReplay()
{
    m_replayTimer->stop();
}

bool RecordManager::isReplaying() const
{
    return m_replayTimer->isActive();
}

int RecordManager::replayFrameCount() const
{
    return m_replayFrames.size();
}

int RecordManager::replayProgress() const
{
    return m_replayIndex;
}

void RecordManager::onReplayTick()
{
    if (m_replayIndex >= m_replayFrames.size()) {
        // 帧发完：停止定时器，通知 UI 收尾（进度条置满、按钮复位）
        stopReplay();
        emit replayFinished();
        return;
    }
    emit replayData(m_replayFrames.at(m_replayIndex));
    ++m_replayIndex;
    emit progressChanged(m_replayIndex);   // 通知 UI 刷新进度条
}

} // namespace services
} // namespace datascope
