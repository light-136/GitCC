/**
 * @file errorcode.cpp
 * @brief V3 领域层 —— 错误码中文名映射实现
 *
 * ── 开发思路 ──
 * 枚举 → 中文名的映射只写一份、集中在领域层。上层（UI/日志）永远调
 * errorCodeName() 拿文本，而不是各自维护一份 if/else 或 switch，避免
 * "改个错误描述要全局搜替换"的散弹式修改。
 */

#include "domain/errorcode.h"

namespace dscope {
namespace domain {

QString errorCodeName(ErrorCode code)
{
    // QStringLiteral 在编译期创建常量字符串（char16_t 数组），运行时零拷贝，
    // 比 QString("...") 更省一次堆分配 —— 这种高频调用的展示函数值得抠这点。
    switch (code) {
    case ErrorCode::Ok:
        return QStringLiteral("成功");
    case ErrorCode::ConnectTimeout:
        return QStringLiteral("连接超时");
    case ErrorCode::ConnectionRefused:
        return QStringLiteral("连接被拒");
    case ErrorCode::CrcError:
        return QStringLiteral("CRC校验错误");
    case ErrorCode::ParseError:
        return QStringLiteral("解析错误");
    case ErrorCode::ReceiveTimeout:
        return QStringLiteral("接收超时");
    case ErrorCode::InvalidFrame:
        return QStringLiteral("非法帧");
    }

    // 防御分支：switch 已覆盖全部枚举，理论不可达；但强类型枚举可被
    // static_cast 成非法值，保留兜底保证任何情况都返回有意义文本。
    return QStringLiteral("未知错误");
}

} // namespace domain
} // namespace dscope
