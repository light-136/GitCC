// ============================================================
//  协议帧数据结构 + 帧构造工厂
//
//  【Qt知识点】值类型类 vs QObject：
//  本类是"数据载体"，不需要信号槽，因此不继承 QObject ——
//  直接作为值类型拷贝传递，性能好、语义清晰。
//  对应 WPF 版 ProtocolFrame.cs（class）但使用 C++ 值语义。
//
//  帧格式：
//  | SOF(1B) | FUNC(1B) | CMD(2B,大端) | DATALEN(2B,小端) | DATA(NB) | CRC(2B,小端) |
//  CRC 校验范围：从 SOF 到 DATA 的所有字节
// ============================================================
#ifndef PROTOCOLFRAME_H
#define PROTOCOLFRAME_H

#include <QByteArray>
#include <QMetaType>
#include <QtGlobal>
#include "CommandDefinitions.h"

/// <summary>
/// 协议帧结构体（值类型）
/// </summary>
class ProtocolFrame
{
public:
    // 帧头（固定 0x01）
    quint8 sof = FrameConstants::SOF;

    // 功能码（固定 0x06）
    quint8 func = FrameConstants::FUNC;

    // 指令码
    quint16 cmd = 0;

    // 有效数据
    QByteArray data;

    // CRC 校验值（解析时填充）
    quint16 crc = 0;

    /// <summary>
    /// 将协议帧序列化为字节数组（用于发送）
    /// </summary>
    QByteArray serialize() const;

    /// <summary>
    /// 从字节数组反序列化协议帧（用于接收解析）
    /// </summary>
    /// <param name="buffer">完整帧字节数据</param>
    /// <param name="ok">输出：是否解析成功（帧头错误/长度不足/CRC 错误均失败）</param>
    static ProtocolFrame deserialize(const QByteArray &buffer, bool *ok = nullptr);
};

// 注册为 Qt 元类型：允许 ProtocolFrame 作为参数在队列连接（跨线程信号槽）中传递
Q_DECLARE_METATYPE(ProtocolFrame)

/// <summary>
/// 协议帧构造工厂 — 快速创建各种指令帧
/// 对应 WPF 版 FrameBuilder 静态类
/// </summary>
class FrameBuilder
{
public:
    /// <summary>
    /// 构建升级请求帧（CMD=0x0010）
    /// </summary>
    /// <param name="requestType">请求类型：0x01=升级请求，0x02=查询状态</param>
    static QByteArray buildUpgradeRequest(quint8 requestType);

    /// <summary>
    /// 构建启动升级帧（CMD=0x0011）
    /// </summary>
    /// <param name="fileSize">固件文件总大小（字节）</param>
    static QByteArray buildUpgradeStart(quint32 fileSize);

    /// <summary>
    /// 构建发送升级数据帧（CMD=0x0012）
    /// </summary>
    /// <param name="offsetAddr">当前数据在固件文件中的偏移地址</param>
    /// <param name="data">升级数据（不超过 128 字节）</param>
    static QByteArray buildUpgradeData(quint32 offsetAddr, const QByteArray &data);

    /// <summary>
    /// 构建完成升级帧（CMD=0x0013）
    /// </summary>
    static QByteArray buildJumpApp();

    /// <summary>
    /// 构建版本号查询帧（CMD=0x0014）
    /// </summary>
    static QByteArray buildVersionRequest();
};

#endif // PROTOCOLFRAME_H
