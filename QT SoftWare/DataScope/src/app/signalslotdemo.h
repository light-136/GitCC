/**
 * @file signalslotdemo.h
 * @brief 信号槽五种连接写法教学演示类（P4 核心）
 *
 * 设计意图：
 *   - 一个"纯 QObject、不依赖 UI"的类，既能在主窗口演示，也能被单测（QSignalSpy）
 *     直接验证——教学点与可测性兼得；
 *   - 每个连接方式写一个独立的私有演示函数，说明文本通过 messageEmitted 信号发出，
 *     由 UI 或测试消费；
 *   - 对应 WPF 的 event/delegate：C# 里"+= 事件处理器"只有一种写法，
 *     Qt 的 connect 有五种，本类逐一演示。
 *
 * P4 重点理解（结合 cpp 注释）：
 *   1. connect 的上下文：sender、signal、receiver、slot；
 *   2. AutoConnection 规则：同线程=直接调用，跨线程=排队投递；
 *   3. emit 只是"发信号"，真正的分发由 moc 生成的元对象代码完成。
 */

#pragma once

#include <QObject>
#include <QString>

// 前向声明，避免在头文件引入 QTimer 完整定义（降低编译耦合）
class QTimer;

namespace datascope {
namespace app {

/**
 * @class SignalSlotDemo
 * @brief 信号槽教学演示对象
 */
class SignalSlotDemo : public QObject
{
    Q_OBJECT

public:
    /**
     * @brief 构造
     * @param parent 父对象（QObject 对象树托管本对象生命周期）
     */
    explicit SignalSlotDemo(QObject *parent = nullptr);

    /** @brief 依次运行五种连接方式演示，说明文本经 messageEmitted 发出 */
    void runAllDemos();

    /**
     * @brief 启动内部时钟：每秒发出一次 timeTicked("HH:mm:ss")
     * @note 演示 QTimer 事件驱动的本质：没有事件循环，定时器永远不会触发
     */
    void startClock();

signals:
    /** @brief 演示用消息通道（携带文本说明） */
    void messageEmitted(const QString &text);

    /** @brief 演示用计数信号（int 参数，用于多种连接演示） */
    void counterChanged(int value);

    /** @brief 演示"信号→信号"转发：valueProduced 转发为 valueForwarded */
    void valueProduced(int value);
    void valueForwarded(int value);

    /** @brief 时钟信号（每秒一次，携带当前时间） */
    void timeTicked(const QString &timeText);

public slots:
    /**
     * @brief counterChanged 的对应槽：收到后转发一条说明消息
     * @note public slots 才能被外部 connect/单测访问；在 Qt5 时代普通成员函数
     *       也可作槽，但 public slots 语义更清晰（保留用于教学对照）
     */
    void onCounterChanged(int value);

private:
    void demoClassicConnect();  // 方式1：成员函数指针（推荐，编译期检查）
    void demoLambdaConnect();   // 方式2：lambda 匿名槽
    void demoSignalForward();   // 方式3：信号 → 信号（转发）
    void demoDisconnect();      // 方式4：连接后断开
    void demoDeleteLater();     // 方式5：deleteLater 与对象生命周期

    QTimer *m_clock = nullptr;  // 内部时钟（挂 parent，由对象树回收）
};

} // namespace app
} // namespace datascope
