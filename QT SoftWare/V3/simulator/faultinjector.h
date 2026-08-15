/**
 * @file faultinjector.h
 * @brief 模拟设备异常注入器（V3）：把"正常帧"变换为"异常字节流"
 *
 * ────────────────────────────────────────────────────────────
 * 为什么需要这个类
 * ────────────────────────────────────────────────────────────
 * 采集端要健壮，就必须经得起"真实链路异常"的考验：半包/粘包、坏 CRC、
 * 非法帧头、链路混入垃圾。这类异常在正常联调中不会出现，只有主动注入
 * 才能稳定复现。本类把"异常变换"做成**纯函数**（输入一段正常帧，输出
 * 0..N 段异常字节流），供 SimulatorDevice（网络层）与集成测试复用。
 *
 * 为什么必须是纯函数（无状态、无 TCP 依赖）：
 *   1. 可单测：直接构造正常帧喂进去，断言输出字节符合异常语义，无需起网络；
 *   2. 可复用：SimulatorDevice 与集成测试共用同一份注入逻辑；
 *   3. 可复现：同一输入必然同一输出，测试结果确定、不 flaky。
 *
 * ── 支持的五类异常（FaultType）──
 *   Sticky       粘包：一帧拆两半分两次发送（测接收端半包拼接）
 *   Fragment     分包：两帧拼接成一段一次发送（测接收端粘包拆分）
 *   CrcError     坏CRC：篡改 DATA 段首字节（测接收端 CRC 拒绝）
 *   InvalidFrame 非法帧：篡改 LEN 字段为超大值（测接收端越界防御）
 *   GarbagePrefix 突变：在合法帧前注入垃圾前缀（测接收端重同步）
 *
 * ── V3 帧格式偏移（7 字节帧头）──
 *   SOF(2) | VERSION(1) | FUNC(1) | CMD(1) | LEN(2 大端) | DATA | CRC16(2)
 *   LEN 字段在偏移 [5,6]；DATA 从下标 7 起；CRC 在末尾 2 字节。
 */

#pragma once

#include <QByteArray>
#include <QVector>
#include <QtGlobal>

namespace dscope {
namespace simulator {

/**
 * @enum FaultType
 * @brief 异常类型枚举（每种对应一种"接收端必须健壮应对"的链路故障）
 *
 * 命名沿用 V2 资产；每类异常都是确定性的字节变换（见 FaultInjector::inject），
 * 不依赖随机数、计数器或时间，保证同一输入同一输出。
 */
enum class FaultType {
    None,           ///< 无异常：原样返回一帧
    Sticky,         ///< 粘包：一帧拆两半，分两次发送（接收端拼半包）
    Fragment,       ///< 分包：两帧拼接成一段，一次发送（接收端拆粘包）
    CrcError,       ///< 坏CRC：篡改 DATA 段首字节，校验必失败
    InvalidFrame,   ///< 非法帧：篡改 LEN 字段（超过 kMaxPayloadLen）
    GarbagePrefix,  ///< 突变：注入垃圾前缀（测帧头重同步）
};

/**
 * @class FaultInjector
 * @brief 正常帧 → 异常字节流的纯函数变换器（全部静态方法，无实例状态）
 *
 * 用法：
 *   const QVector<QByteArray> chunks = FaultInjector::inject(FaultType::CrcError, frame);
 *   for (const QByteArray &chunk : chunks) socket->write(chunk);  // 每段一次 write
 *
 * 边界：纯逻辑，不依赖 QtNetwork；同一 (type, normalFrame) 永远返回同一结果。
 */
class FaultInjector
{
public:
    /**
     * @brief 把一段正常帧变换为 0..N 段异常字节流（核心注入函数，纯函数）
     * @param type        要注入的异常类型
     * @param normalFrame 构建好的正常协议帧字节（含 SOF..CRC）
     * @return 0..N 段字节（每段对应一次 socket write）；空向量 = 本次不发送
     *
     * @note 无状态、确定性：同一 (type, normalFrame) 永远返回同一结果。
     *       帧过短无法注入时，退化为原样返回一帧（防御，不抛错）。
     */
    static QVector<QByteArray> inject(FaultType type, const QByteArray &normalFrame);

    /**
     * @brief 轮询序列中的下一个异常类型（fault 模式循环注入用）
     * @param current 当前类型
     * @return 下一类型；current 为 None/未知时从 Sticky 开始
     *
     * 轮询顺序：Sticky → Fragment → CrcError → InvalidFrame → GarbagePrefix → Sticky…
     */
    static FaultType nextType(FaultType current);

    /**
     * @brief 异常类型的中文名称（日志/命令行展示用）
     * @param type 异常类型
     * @return 中文名称（指向静态字符串，调用方勿释放）
     */
    static const char *typeName(FaultType type);

private:
    /** @brief 坏 CRC：篡改 DATA 段首字节（帧头之后第一个字节，按位取反） */
    static QByteArray corruptDataByte(const QByteArray &frame);

    /** @brief 非法帧：把 LEN 字段（偏移 5、6 大端）改为 0xFFFF */
    static QByteArray corruptLenField(const QByteArray &frame);

    /** @brief 突变：构造固定垃圾前缀（含一个游离 0xAA，测重同步） */
    static QByteArray garbageBytes();
};

} // namespace simulator
} // namespace dscope
