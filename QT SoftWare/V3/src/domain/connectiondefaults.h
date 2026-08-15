/**
 * @file connectiondefaults.h
 * @brief V3 领域层 —— 默认连接参数（header-only 单一常量源）
 *
 * ── 开发思路 ──
 * 主机/端口若散落成 `"127.0.0.1"`、`45678` 这类魔法数，UI 与 Simulator 各自
 * 硬编码，一旦要换默认值就得同时改多处、极易漏改导致两端默认端口不一致
 * （连不上还得猜为什么）。这里把默认连接参数收敛为唯一具名常量：
 *   - UI（MainWindow）用它做工具栏默认值；
 *   - Simulator（main.cpp）用它做监听端口缺省值；
 *   - 集成测试用它做回环地址，保证"测试 ↔ 设备 ↔ 界面"三者默认值永远同源。
 *
 * ── 为什么放 domain 层 ──
 * host/port 是"连接/传输"语义，属领域约束而非协议帧字段，放 domain 最贴切；
 * 且本文件是纯 constexpr 常量（header-only，内联），任何只 include 头、未链接
 * dscope_domain 的 target（如 simulator 只链接 dscope_protocol）也能安全引用。
 *
 * ── WPF 对照 ──
 *   相当于 C# 里的 public static class ConnectionDefaults { public const string
 *   Host = "127.0.0.1"; public const ushort Port = 45678; }。
 */

#pragma once

#include <QtGlobal>

namespace dscope {
namespace domain {

/** @brief 默认主机地址：回环地址（本机联调，Simulator 与 DataScope 同机跑） */
constexpr const char *kDefaultHost = "127.0.0.1";

/** @brief 默认端口：与 Simulator 的监听端口保持一致（TCP 数据链路） */
constexpr quint16 kDefaultPort = 45678;

} // namespace domain
} // namespace dscope
