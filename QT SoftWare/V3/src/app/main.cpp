/**
 * @file main.cpp
 * @brief V3 程序入口 —— Composition Root（组装根）
 *
 * ── 为什么这里叫 Composition Root ──
 * 全工程只有这一处"知道所有具体类并组装它们"。其余各层（domain/protocol/
 * acquisition/ui）彼此不直接 new 对方，只通过接口、信号槽、依赖注入协作。
 * 这样做的好处：换实现、换线程模型、写集成测试时，只需改这一处装配代码，
 * 业务层零感知。
 *
 * ── 线程模型（权威规格：1 主线程 + 1 采集线程）──
 *   - 主线程：QApplication 事件循环 + MainWindow（渲染 + 交互）；
 *   - 采集线程：QThread 承载 AcquisitionWorker（socket I/O + 帧解析都在这跑）。
 *   跨线程协作全部走 QueuedConnection：worker 信号 → UI 槽、UI invokeMethod → worker 槽。
 *
 * ── 组装顺序（顺序即正确性，不可颠倒）──
 *   1. 先建 worker：其构造函数内 qRegisterMetaType 登记 QVector<DataPoint>/<ChannelConfig>，
 *      MainWindow 用字符串式 SIGNAL/SLOT 连接这些跨线程信号时，签名才能被解析；
 *   2. 再 moveToThread：worker 连同内部 QTcpSocket/QTimer 一起迁到采集线程；
 *   3. 最后建 MainWindow：构造注入 worker，内部 connect/invokeMethod 都基于已注册的元类型。
 *
 * ── 关停顺序（先停采集、再退线程、随栈析构）──
 *   1. BlockingQueuedConnection 调 stop：确保 stop 槽在采集线程内执行完毕
 *      （abort socket、停定时器、状态落 Disconnected），而非"投递了就不管"；
 *   2. quit + wait：退出采集线程事件循环并等待线程真正结束；
 *   3. 随栈析构：stop() 已把定时器停掉、套接字 abort，析构里的清理都是 no-op，
 *      因此 worker 在栈上安全析构，无需 moveToThread（Qt 禁止从非所属线程移动对象）。
 *
 * ── WPF 对照 ──
 *   Composition Root  ↔  WPF 的 App.xaml.cs 里组装 ViewModel/Service 的地方；
 *   QThread + moveToThread  ↔  Task.Run + SynchronizationContext 绑定后台线程。
 */

#include <QApplication>
#include <QCoreApplication>
#include <QDebug>
#include <QMetaObject>
#include <QThread>

#include <cstdio>

#include "acquisition/acquisitionworker.h"
#include "infra/logger.h"
#include "protocol/crc16.h"
#include "protocol/framebuilder.h"
#include "protocol/frameparser.h"
#include "protocol/frametypes.h"
#include "ui/mainwindow.h"
#include "version.h"

#ifdef Q_OS_WIN
#  ifndef NOMINMAX
#    define NOMINMAX
#  endif
#  include <windows.h>   // AttachConsole / ATTACH_PARENT_PROCESS：GUI 程序输出到父控制台
#endif

namespace {

/**
 * @brief 协议级自检：验证 CRC 标准向量 + 帧往返，供 e2e 探针（--selftest）调用
 * 开发思路：发布前用最小编码验证 protocol 库链接正确、字节序无漂移，
 *           而非重复单元测试全量断言——这是"烟雾自检"，不是"重复造轮子"。
 */
bool runSelfTest()
{
    using namespace dscope::protocol;

    // 1. MODBUS CRC16 标准向量："123456789" → 0x4B37
    if (Crc16::compute(QByteArray("123456789")) != quint16(0x4B37))
        return false;

    // 2. 帧构建→解析往返：payload 逐字节一致
    const QByteArray payload = QByteArray::fromHex("010203");
    const QByteArray frame  = FrameBuilder::build(kFuncData, 0x01, payload);
    if (frame.isEmpty())
        return false;

    FrameParser parser;
    parser.feed(frame);
    ParsedFrame parsed;
    FrameError err;
    if (parser.next(parsed, err) != FrameParser::Status::Frame)
        return false;
    if (parsed.payload != payload)
        return false;

    return true;
}

} // namespace

int main(int argc, char *argv[])
{
    // ── 0. 命令行前置处理：--version / --selftest（启动 GUI 前短路）──
    // DataScope 是 WIN32 无控制台程序，stdout 默认不可见；这里附加到父进程控制台
    // 让 ctest 的 e2e 探针能读到输出并断言（DoD R4）。
    if (argc > 1) {
        const QString arg = QString::fromLocal8Bit(argv[1]);
        if (arg == QLatin1String("--version")) {
#ifdef Q_OS_WIN
            AttachConsole(ATTACH_PARENT_PROCESS);
#endif
            std::fprintf(stdout, "DataScope Studio %s\n", V3_VERSION_STRING);
            return 0;
        }
        if (arg == QLatin1String("--selftest")) {
#ifdef Q_OS_WIN
            AttachConsole(ATTACH_PARENT_PROCESS);
#endif
            const bool ok = runSelfTest();
            std::fprintf(stdout, ok ? "SELFTEST OK\n" : "SELFTEST FAIL\n");
            return ok ? 0 : 1;
        }
    }

    QApplication app(argc, argv);
    QApplication::setApplicationName(QStringLiteral("DataScope Studio"));
    // 版本号唯一来源：CMake configure_file 生成的 version.h（禁止手写版本字符串）
    QApplication::setApplicationVersion(QStringLiteral(V3_VERSION_STRING));

    // ── 0. 安装日志：GUI 程序无控制台，qDebug/qWarning 需重定向到文件才可见 ──
    // 日志落在 exe 同目录 logs/datascope.log；必须在任何线程启动前调用（单次）。
    dscope::infra::Logger::install(
        QCoreApplication::applicationDirPath() + QStringLiteral("/logs"));

    // 记录启动日志：验证 Logger 已生效（落盘到 logs/datascope.log）
    qInfo().noquote()
        << QStringLiteral("DataScope Studio V3 %1 启动，采集线程待命")
               .arg(QStringLiteral(V3_VERSION_STRING));

    // ── 1. 采集层：先建 worker（构造时注册跨线程元类型），再迁到采集线程 ──
    dscope::acquisition::AcquisitionWorker worker;   // 栈对象，构造即完成元类型登记

    QThread workerThread;
    worker.moveToThread(&workerThread);              // worker + socket + 定时器 一起迁线程
    workerThread.setObjectName(QStringLiteral("采集线程"));
    workerThread.start();

    // ── 2. UI 层：构造注入 worker（此时元类型已就绪，字符串连接可解析签名）──
    dscope::ui::MainWindow window(&worker);
    window.show();

    const int ret = app.exec();

    // ── 3. 关停：停止采集 → 退线程 → 随栈析构 ──────────────────────
    // 用 BlockingQueuedConnection 保证 stop 真正在采集线程执行完，避免 quit 抢先。
    // stop() 已停定时器 + abort 套接字，随后的析构清理全是 no-op，栈对象安全析构。
    QMetaObject::invokeMethod(&worker, "stop", Qt::BlockingQueuedConnection);
    workerThread.quit();
    // 兜底：5s 内未退出说明采集线程可能被 socket 阻塞（极端异常），记录告警后
    // 强制 terminate 再 wait，避免程序退出被永久卡住。正常路径 stop() 已 abort
    // 套接字、停定时器，wait 会立即返回，不会走到 terminate 分支。
    if (!workerThread.wait(5000)) {
        qWarning().noquote() << QStringLiteral("采集线程 5s 内未退出，强制终止线程");
        workerThread.terminate();
        workerThread.wait();
    }

    return ret;
}
