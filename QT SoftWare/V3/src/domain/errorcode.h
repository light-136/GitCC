/**
 * @file errorcode.h
 * @brief V3 领域层 —— 错误码枚举与中文名映射（头文件）
 *
 * ── 开发思路 ──
 * 采集/协议层的失败原因如果散落成一个个布尔值或 int 魔数（例如 -1=超时、
 * -2=CRC、-3=解析），调用方很难判断"到底为什么失败"，日志与 UI 也只能显示
 * 一串看不懂的数字。这里把领域内所有可预见的失败收敛为一个强类型枚举
 * ErrorCode，并配套 errorCodeName() 把枚举翻译成中文，供日志、状态栏、
 * 错误弹窗统一使用 —— 错误语义内聚到领域层，UI 只负责"显示文本"。
 *
 * ── WPF 对照 ──
 *   enum class ErrorCode  ↔  C# 的强类型枚举：必须写 ErrorCode::Ok，不做隐式
 *                            整型转换，从源头避免把"成功"误传成数字 0 的坑。
 *   errorCodeName()       ↔  C# 中枚举的 ToString() + 资源字典本地化；
 *                            对应 WPF 里把"枚举 → 显示文本"做成纯函数/转换器。
 */

#pragma once

#include <QString>

namespace dscope {
namespace domain {

/**
 * @enum ErrorCode
 * @brief 领域错误码（采集/协议/传输各环节的统一失败描述）
 */
enum class ErrorCode {
    Ok,                  ///< 成功（无错误）
    ConnectTimeout,      ///< 连接超时
    ConnectionRefused,   ///< 连接被拒
    CrcError,            ///< CRC 校验错误
    ParseError,          ///< 解析错误
    ReceiveTimeout,      ///< 接收超时
    InvalidFrame         ///< 非法帧
};

/**
 * @brief 把错误码翻译成中文名（供日志、状态栏、错误弹窗统一使用）
 * @param code 错误码
 * @return 中文名："成功"/"连接超时"/"连接被拒"/"CRC校验错误"/"解析错误"/
 *         "接收超时"/"非法帧"；未识别的值返回"未知错误"
 * @note 这是纯函数（无副作用、不修改任何状态），实现见 errorcode.cpp。
 *       WPF 对照：等价于一个无副作用的 static 方法 / 值转换器。
 */
QString errorCodeName(ErrorCode code);

} // namespace domain
} // namespace dscope
