// ============================================================
//  GD32低压驱动器Boot升级协议 — 指令与状态常量定义
//
//  【Qt知识点】命名空间（namespace）组织常量：
//  C# 用 static class 存常量，C++ 惯用 namespace + const。
//  这些常量与 WPF 版 CommandDefinitions.cs 逐一对等。
//
//  协议帧格式：
//  | SOF(1B) | FUNC(1B) | CMD(2B大端) | DATALEN(2B小端) | DATA(NB) | CRC(2B小端) |
//  | 0x01    | 0x06     | 指令码      | 数据长度        | 有效数据  | CRC16/Modbus |
// ============================================================
#ifndef COMMANDDEFINITIONS_H
#define COMMANDDEFINITIONS_H

#include <QtGlobal>   // quint8/quint16 等跨平台定宽整数类型

// ---------- 协议帧固定字段常量 ----------
namespace FrameConstants {
    // 帧头固定值
    const quint8 SOF = 0x01;

    // 功能码固定值
    const quint8 FUNC = 0x06;

    // 帧头长度: SOF(1) + FUNC(1) + CMD(2) + DATALEN(2) = 6 字节
    const int HEADER_SIZE = 6;

    // CRC 校验字节数
    const int CRC_SIZE = 2;

    // 升级数据每帧最大有效载荷（协议规定不超过 128 字节）
    const int MAX_DATA_PAYLOAD = 128;
}

// ---------- 协议指令码（CMD 字段） ----------
namespace CommandCode {
    const quint16 UPGRADE_REQ   = 0x0010;   // 升级请求：询问驱动器是否具备升级条件
    const quint16 UPGRADE_START = 0x0011;   // 启动升级：发送文件大小，驱动器擦除 Flash
    const quint16 UPGRADE_DATA  = 0x0012;   // 发送升级数据：将固件数据写入 Flash
    const quint16 JUMP_APP      = 0x0013;   // 完成升级：驱动器从 Boot 跳转 App
    const quint16 VER_REQ       = 0x0014;   // 请求版本号：查询 Bootloader 版本号
}

// ---------- 升级请求子命令（UPGRADE_REQ 的 DATA[0]） ----------
namespace UpgradeRequestType {
    const quint8 REQUEST_UPGRADE = 0x01;    // 升级状态请求（请求进入 Boot 并准备升级）
    const quint8 QUERY_STATUS    = 0x02;    // 查询当前处于何种状态
}

// ---------- 升级请求的回复状态码（UPGRADE_REQ 回复 DATA[0]） ----------
namespace UpgradeRequestResponse {
    const quint8 IN_BOOT_READY       = 0x00;  // 当前处于 Boot 中，具备升级条件
    const quint8 IN_APP_JUMPING      = 0x01;  // 当前处于 App 中，正在跳转到 Boot
    const quint8 IN_APP_STATUS_ONLY  = 0x02;  // 当前处于 App 中，只回复状态不做处理
    const quint8 IN_BOOT_STATUS_ONLY = 0x03;  // 当前处于 Boot 中，只回复状态不做处理
}

// ---------- 通用回复状态码 ----------
namespace ResponseStatus {
    const quint8 SUCCESS = 0x00;   // 操作成功
    const quint8 FAILURE = 0x01;   // 操作失败
}

#endif // COMMANDDEFINITIONS_H
