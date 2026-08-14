/**
 * @file tst_recordmanager.cpp
 * @brief 数据记录与回放管理单测（P15）—— Qt Test 框架
 *
 * 覆盖 RecordManager 四个能力：
 *   1. 记录：CSV 落盘内容正确（表头 + 逐点行）；
 *   2. 读回：按时间戳重组为帧，帧数正确；
 *   3. 回放：replayData 信号按帧触发 → replayFinished 收尾；
 *   4. 记录开关状态切换。
 *
 * 教学点（对照 C#）：
 *   - QTemporaryDir 测试临时目录 ≈ C# 的 Path.GetTempPath + 随机子目录；
 *   - QSignalSpy 监听回放信号，验证"定时逐帧"行为。
 */

#include <QtTest>

#include "services/recordmanager.h"
#include "domain/models.h"

#include <QTemporaryDir>
#include <QFile>
#include <QTextStream>
#include <QDateTime>

using datascope::domain::DataPoint;
using datascope::services::RecordManager;

// ---- 元类型：QSignalSpy 携带 replayData 参数需要 ----
Q_DECLARE_METATYPE(datascope::domain::DataPoint)
Q_DECLARE_METATYPE(QVector<datascope::domain::DataPoint>)

namespace {
/**
 * @brief 构造一帧采集数据（4 通道，同一时间戳，模拟采集帧）
 * @param base 帧序号基值：决定时间戳（每帧 +1s）与数值（base+通道号）
 */
QVector<DataPoint> makeFrame(double base)
{
    const qint64 ts = 1734168000000LL + static_cast<qint64>(base) * 1000;
    QVector<DataPoint> pts;
    for (int ch = 0; ch < 4; ++ch) {
        DataPoint dp;
        dp.channelIndex = ch;
        dp.value        = base + ch;   // 数值可区分通道
        dp.timestamp    = QDateTime::fromMSecsSinceEpoch(ts);
        pts.append(dp);
    }
    return pts;
}

/** @brief 用 RecordManager 写入一 2 帧记录文件，返回路径 */
QString writeTwoFrameCsv(QTemporaryDir &dir, RecordManager &mgr)
{
    const QString path = dir.filePath("rec.csv");
    mgr.startRecording(path);
    mgr.appendData(makeFrame(0));
    mgr.appendData(makeFrame(1));
    mgr.stopRecording();
    return path;
}
} // namespace

// 测试类：每个 private slot 一个独立用例
class TestRecordManager : public QObject
{
    Q_OBJECT

private slots:
    void initTestCase();           // 全局初始化：注册元类型
    void recordWritesCsv();        // 记录：CSV 内容正确
    void loadReplayBuildsFrames(); // 读回：按时间戳重组帧
    void replayEmitsFramesAndFinished();  // 回放：逐帧发送 + 收尾信号
    void recordingStateToggle();   // 记录开关状态切换
};

void TestRecordManager::initTestCase()
{
    // replayData(QVector<DataPoint>) 参数跨 QSignalSpy 拷贝需元类型（与 dataservice 一致）
    qRegisterMetaType<datascope::domain::DataPoint>("datascope::domain::DataPoint");
    qRegisterMetaType<QVector<datascope::domain::DataPoint>>("QVector<datascope::domain::DataPoint>");
}

// ================= ① 记录：CSV 落盘内容正确 =================

void TestRecordManager::recordWritesCsv()
{
    QTemporaryDir dir;
    QVERIFY(dir.isValid());

    RecordManager mgr;
    QSignalSpy errSpy(&mgr, &RecordManager::errorOccurred);
    const QString path = writeTwoFrameCsv(dir, mgr);
    QVERIFY(!path.isEmpty());

    // 文件内容：表头 1 行 + 2 帧×4 通道 = 8 行数据，共 9 非空行
    QFile f(path);
    QVERIFY(f.open(QIODevice::ReadOnly | QIODevice::Text));
    const QString content = QString::fromUtf8(f.readAll());
    f.close();

    const QStringList lines = content.split('\n', QString::SkipEmptyParts);
    QCOMPARE(lines.size(), 9);
    QVERIFY(lines.at(0).startsWith(QStringLiteral("timestampMs")));   // 表头正确
    QCOMPARE(errSpy.count(), 0);   // 全程无错误
}

// ================= ② 读回：按时间戳重组成帧 =================

void TestRecordManager::loadReplayBuildsFrames()
{
    QTemporaryDir dir;
    RecordManager writer;
    const QString path = writeTwoFrameCsv(dir, writer);

    RecordManager reader;
    QVERIFY(reader.loadReplay(path));
    QCOMPARE(reader.replayFrameCount(), 2);   // 2 个时间戳 = 2 帧
}

// ================= ③ 回放：逐帧发送 + 收尾信号 =================

void TestRecordManager::replayEmitsFramesAndFinished()
{
    QTemporaryDir dir;
    RecordManager writer;
    const QString path = writeTwoFrameCsv(dir, writer);

    RecordManager player;
    QVERIFY(player.loadReplay(path));

    QSignalSpy dataSpy(&player, &RecordManager::replayData);
    QSignalSpy doneSpy(&player, &RecordManager::replayFinished);

    player.startReplay(10);   // 10ms/帧：2 帧 ~20ms 播完
    QVERIFY(player.isReplaying());
    QTRY_COMPARE_WITH_TIMEOUT(doneSpy.count(), 1, 3000);   // 播完即收尾
    QCOMPARE(dataSpy.count(), 2);                          // 恰好发完 2 帧
    QVERIFY(!player.isReplaying());                        // 定时器已停

    // 抽验最后一帧内容：4 通道、数值与记录时一致
    const QVariantList lastArgs = dataSpy.takeLast();
    const QVector<DataPoint> last = lastArgs.at(0).value<QVector<DataPoint>>();
    QCOMPARE(last.size(), 4);
    QCOMPARE(last.at(0).channelIndex, 0);
    QVERIFY(qAbs(last.at(0).value - 1.0) < 1e-6);   // 第二帧 base=1 → 通道0 值 1.0
}

// ================= ④ 记录开关状态切换 =================

void TestRecordManager::recordingStateToggle()
{
    QTemporaryDir dir;
    RecordManager mgr;

    QVERIFY(!mgr.isRecording());                          // 初始：未记录
    QVERIFY(mgr.startRecording(dir.filePath("rec.csv"))); // 开始：变真
    QVERIFY(mgr.isRecording());
    mgr.stopRecording();                                  // 停止：变假
    QVERIFY(!mgr.isRecording());
}

// 生成 main()：QTEST_MAIN 为测试类生成独立可执行文件入口
QTEST_MAIN(TestRecordManager)
#include "tst_recordmanager.moc"
