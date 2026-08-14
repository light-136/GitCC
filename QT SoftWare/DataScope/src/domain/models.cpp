/**
 * @file models.cpp
 * @brief 领域模型实现（P8 Domain 层）
 *
 * 与头文件的关系：
 *   - 头文件只声明，本文件给出全部成员函数的定义，
 *     保证"实现只编译一次"（对应 C# 把声明与实现分开的惯例，也利于加快构建）。
 *   - 本文件不含任何 Q_OBJECT 宏（纯数据类，不需要元对象系统 / moc 参与）。
 *
 * 隐式共享（Copy-on-Write）细节：
 *   - QVector 与 QString 底层采用"引用计数 + 写时复制"：
 *       拷贝一个 Device 时，m_channels 只是把引用计数 +1，底层数组不复制；
 *       随后对副本调用 addChannel()/clearChannels() 时才真正复制数组（detach）。
 *     这正是"拷贝便宜、互不影响"的实现基础（见单测 device_copyIndependent）。
 */

#include "domain/models.h"

namespace datascope {
namespace domain {

// ==================== 只读访问 ====================

QString Device::id() const
{
    // const 成员函数：只读自身成员，保证调用方拿到的是一份稳定的只读视图
    return m_id;
}

QString Device::name() const
{
    return m_name;
}

DeviceStatus Device::status() const
{
    return m_status;
}

int Device::channelCount() const
{
    // QVector::size() 返回 int，正好满足契约的 int 返回类型
    return m_channels.size();
}

const QVector<Channel> &Device::channels() const
{
    // 返回 const 引用：外部只能读，不能改；
    // 也不产生数组拷贝（如果返回 QVector 值，则每次调用都深拷贝一次）
    return m_channels;
}

// ==================== 写操作 ====================

void Device::setId(const QString &id)
{
    m_id = id;
}

void Device::setName(const QString &name)
{
    m_name = name;
}

void Device::setStatus(DeviceStatus status)
{
    m_status = status;
}

void Device::addChannel(const Channel &ch)
{
    // 追加到内部数组。若该 Device 之前被拷贝过（共享底层数据），
    // 这里的 append 会自动触发"写时复制"（detach），保证不影响其他副本。
    m_channels.append(ch);
}

void Device::clearChannels()
{
    // 清空通道；同样会先 detach 再清空，确保共享副本各自独立。
    m_channels.clear();
}

} // namespace domain
} // namespace datascope
