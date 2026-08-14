/**
 * @file signalslotdemo.cpp
 * @brief 信号槽五种连接方式教学演示实现（P4 核心）
 *
 * 设计思路（对照 WPF）：
 *   - WPF/C# 里事件订阅只有一种写法：`widget.Event += handler;`
 *   - Qt 的 connect 有五种主流写法，本文件逐一演示并加注讲解。
 *
 * P4 必读的三个机制（边读代码边对照）：
 *   1. **connect 的四要素**：sender（发出者）、signal（信号）、
 *      receiver（接收者）、slot（槽/回调）。前三个是"接线"，
 *      第四个是"接到的处理动作"。
 *   2. **AutoConnection 默认规则**：发送者与接收者在同一线程 → 直接调用
 *      （DirectConnection，类似 C# 同步事件）；跨线程 → 排队投递
 *      （QueuedConnection，类似 WPF 跨线程 Dispatcher.Invoke 的排队语义）。
 *      跨线程演示留到 P12（生产者-消费者 Worker），本阶段只讲原理。
 *   3. **emit 只是关键字**：`emit counterChanged(42);` 在 C++ 层面
 *      编译后等价于 `counterChanged(42);`——真正的事件分发由 moc
 *      生成的元对象代码完成，这是"元对象系统"的核心（P4 主题）。
 */

#include "app/signalslotdemo.h"

#include <QDateTime>
#include <QDebug>
#include <QTimer>

namespace datascope {
namespace app {

SignalSlotDemo::SignalSlotDemo(QObject *parent)
    : QObject(parent)
{
    // 构造函数刻意留空：演示用的连接都在 runAllDemos() 里"当场建当场拆"，
    // 教学上更直观（WPF 事件订阅通常写在构造函数里，这里展示另一种风格）。
}

void SignalSlotDemo::runAllDemos()
{
    // 每次运行前清空既有连接，保证演示可重复执行（避免重复连接导致信号触发两次）
    disconnect(this, nullptr, this, nullptr);

    emit messageEmitted("========== 信号槽五种连接演示 ==========");

    demoClassicConnect();  // 方式1：成员函数指针
    demoLambdaConnect();   // 方式2：lambda 匿名槽
    demoSignalForward();   // 方式3：信号 → 信号（转发）
    demoDisconnect();      // 方式4：连接后断开
    demoDeleteLater();     // 方式5：deleteLater 与对象生命周期

    emit messageEmitted("========== 演示结束（可反复点击） ==========");
}

void SignalSlotDemo::startClock()
{
    // 定时器 QTimer 是"事件驱动"的教科书例子：
    //   - 创建定时器，设间隔 1000ms；
    //   - connect(timeout 信号 → 匿名 lambda 发出 timeTicked)；
    //   - start() 后，Qt 事件循环每个 1 秒触发一次。
    // 若程序没有进入事件循环（QApplication::exec()），start() 后什么也不会发生——
    // 这正是"事件驱动"与"多线程死循环"的本质区别。
    if (m_clock)
        return; // 已在运行则直接返回（幂等）

    m_clock = new QTimer(this);                       // 挂 parent，由对象树回收（RAII）
    m_clock->setInterval(1000);

    // 方式2 的典型用法：定时器 → lambda 发新信号，把"Qt 类型"翻译成"业务信号"
    connect(m_clock, &QTimer::timeout, this, [this]() {
        const QString now = QDateTime::currentDateTime().toString("HH:mm:ss");
        emit timeTicked(now);  // 每秒一次
    });

    m_clock->start();
}

void SignalSlotDemo::onCounterChanged(int value)
{
    // 槽：收到 counterChanged 后发一条说明消息（演示"信号 → 槽"闭环）
    emit messageEmitted(QString("   [槽 onCounterChanged] 收到计数 = %1").arg(value));
}

// ---------------------------------------------------------------------------
// 方式1：成员函数指针 connect（Qt5 推荐的"经典写法"）
// ---------------------------------------------------------------------------
void SignalSlotDemo::demoClassicConnect()
{
    emit messageEmitted("【方式1】成员函数指针（推荐，编译期检查签名）");

    // 语法：connect(sender, &SenderClass::signal, receiver, &ReceiverClass::slot);
    // 优点：信号/槽的签名在编译期检查，拼错函数名、参数类型不符会直接编译报错。
    connect(this, &SignalSlotDemo::counterChanged,
            this, &SignalSlotDemo::onCounterChanged);

    emit counterChanged(42);  // 触发 → onCounterChanged(42) 被直接调用

    // 断开方式1（演示用，实际 runAllDemos 开头已统一断开）
    disconnect(this, &SignalSlotDemo::counterChanged,
               this, &SignalSlotDemo::onCounterChanged);
}

// ---------------------------------------------------------------------------
// 方式2：lambda 匿名槽（Qt5 时代最常用，适合"就地处理"）
// ---------------------------------------------------------------------------
void SignalSlotDemo::demoLambdaConnect()
{
    emit messageEmitted("【方式2】lambda 匿名槽（就地处理，最灵活）");

    // lambda 捕获 [this]：在 lambda 体内可使用本对象成员。
    // 生命周期注意：lambda 存活时间 = 连接存活时间，若 SignalSlotDemo 先于
    // 连接被销毁，lambda 里的 this 会悬垂 → P5 用上下文对象解决（对象树）。
    connect(this, &SignalSlotDemo::counterChanged, this, [this](int v) {
        emit messageEmitted(QString("   [lambda] 收到计数 = %1（翻倍后 %2）")
                            .arg(v).arg(v * 2));
    });

    emit counterChanged(7);   // 触发 lambda；同时方式1已断开，不会重复打印

    disconnect(this, &SignalSlotDemo::counterChanged, this, nullptr);
    // 上面写法：断开 counterChanged 的所有连接（receiver=nullptr 表示不限接收者）
}

// ---------------------------------------------------------------------------
// 方式3：信号 → 信号（转发 / 透传）
// ---------------------------------------------------------------------------
void SignalSlotDemo::demoSignalForward()
{
    emit messageEmitted("【方式3】信号 → 信号（转发，适合中间层透传）");

    // valueForwarded 是"转发信号"，它本身没有槽，只是把 valueProduced 透传出去。
    // 典型场景：Service 层收到底层信号，转发给 UI 层（数据流的分层透传）。
    connect(this, &SignalSlotDemo::valueProduced,
            this, &SignalSlotDemo::valueForwarded);

    // 再给"终点"接一个 lambda，证明转发链路完整：
    connect(this, &SignalSlotDemo::valueForwarded, this, [this](int v) {
        emit messageEmitted(QString("   [转发链终点] valueProduced(%1) → valueForwarded → 收到")
                            .arg(v));
    });

    emit valueProduced(100);  // 只发源头信号，两个下游都自动触发

    disconnect(this, nullptr, this, nullptr);  // 清空本对象全部连接
}

// ---------------------------------------------------------------------------
// 方式4：连接后断开（disconnect）
// ---------------------------------------------------------------------------
void SignalSlotDemo::demoDisconnect()
{
    emit messageEmitted("【方式4】连接后断开（disconnect 的三种粒度）");

    // (a) 按"连接句柄"断开：connect 返回 QMetaObject::Connection，可精确断开
    const QMetaObject::Connection conn = connect(this, &SignalSlotDemo::counterChanged,
                                                 this, &SignalSlotDemo::onCounterChanged);
    emit counterChanged(1);           // 会触发
    disconnect(conn);                 // 精确断开这一条
    emit counterChanged(2);           // 不再触发（onCounterChanged 不会打印 2）

    // (b) 按"信号 + 接收者"断开（上一行已演示过完整写法）
    // (c) 按"发送者 + 信号"断开：disconnect(sender, signal, nullptr, nullptr)

    emit messageEmitted("   [验证] 计数 2 发出去后没有任何槽打印 → 断开生效");
}

// ---------------------------------------------------------------------------
// 方式5：deleteLater 与对象生命周期
// ---------------------------------------------------------------------------
void SignalSlotDemo::demoDeleteLater()
{
    emit messageEmitted("【方式5】deleteLater：安全的「延迟删除」机制");

    // 场景：UI 层想删除一个"正在处理中的对象"。若直接 delete，恰逢该对象
    // 此刻正执行槽函数，会崩溃（对应 C# 跨线程 Dispatcher.Invoke 的错误）。
    // deleteLater() 的做法：向事件循环投递一个"删除请求"，当前事件处理完后，
    // 下一次事件循环迭代才真正 delete 对象。
    auto *temp = new QObject(this);   // 临时对象，父对象仍是 this（对象树托管）

    // 监听 destroyed 信号，验证删除确实发生（这正是"信号槽"的又一次应用）
    connect(temp, &QObject::destroyed, this, [this]() {
        emit messageEmitted("   [destroyed] 临时对象已被事件循环真正删除 ✓");
    });

    // 注释【教学重点】：此处不执行 temp->deleteLater()，而是让其随父对象树回收，
    // 因为"删除后信号连接里的 this 悬垂"是 P4 必须避开的坑。
    // 实际 deleteLater 的完整演示见 P12 Worker 生命周期章节。
    Q_UNUSED(temp);
}

} // namespace app
} // namespace datascope
