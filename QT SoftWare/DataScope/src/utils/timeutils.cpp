/**
 * @file timeutils.cpp
 * @brief 时间工具库实现
 *
 * P3 重点：这里演示 Qt 核心值类型的标准用法：
 *   - QDateTime::toString / QTime::fromString 的格式串（format string）；
 *   - msecsTo 的毫秒差语义；
 *   - QString::arg 的宽度补零（类似 C# 的 "D2" 格式）。
 */

#include "utils/timeutils.h"

namespace datascope {
namespace utils {

namespace {
// 文件内私有常量（匿名命名空间 = 该 .cpp 文件内的"内部静态"，类似 C# 的 private const）
// 集中管理格式串，避免魔法字符串散落在各函数里，后续要改格式只动这里
const char kDateTimeFormat[] = "yyyy-MM-dd HH:mm:ss.zzz";
const char kFileTimestampFormat[] = "yyyyMMdd_HHmmss_zzz";

/**
 * @brief 把数值补成固定两位（左侧补 '0'）
 * @param value 要格式化的数值
 * @return 形如 "05"、"12" 的两位串；value 超过两位则原样输出更多位
 * @note QString::arg(value, 2, 10, '0')：第 2 参是最小宽度 2、第 3 参是十进制、
 *       第 4 参是填充字符 '0'，对应 C# 的 value.ToString("D2")
 */
QString pad2(qint64 value)
{
    return QStringLiteral("%1").arg(value, 2, 10, QLatin1Char('0'));
}
} // namespace

QString TimeUtils::formatDateTime(const QDateTime &dt)
{
    return dt.toString(QLatin1String(kDateTimeFormat));
}

QString TimeUtils::nowString()
{
    // 取当前时刻直接复用上面的格式化，保证两者输出格式完全一致
    return formatDateTime(QDateTime::currentDateTime());
}

qint64 TimeUtils::elapsedMicros(const QDateTime &start, const QDateTime &end)
{
    // msecsTo 语义：start.msecsTo(end) = end - start（毫秒，可为负）。
    // ×1000 换算成微秒；相同时刻为 0。假定时间跨度在 qint64 安全范围内。
    return start.msecsTo(end) * 1000;
}

QString TimeUtils::fileTimestamp(const QDateTime &dt)
{
    return dt.toString(QLatin1String(kFileTimestampFormat));
}

bool TimeUtils::parseTime(const QString &text, QTime &out)
{
    // 第一次尝试最常用的 "HH:mm:ss"。
    // Qt 5.12 的 QTime::fromString 遇到格式之外的多余字符（如 ".123"）会严格失败，
    // 因此带毫秒的输入会自然落到第二次尝试，不会被"截断"误解析。
    QTime parsed = QTime::fromString(text, QStringLiteral("HH:mm:ss"));
    if (!parsed.isValid())
        parsed = QTime::fromString(text, QStringLiteral("HH:mm:ss.zzz"));

    if (!parsed.isValid())
        return false;          // 两种格式都失败 → 返回 false，且不修改 out
    out = parsed;              // 成功才写出参，保证"失败不碰 out"
    return true;
}

QString TimeUtils::formatDuration(qint64 seconds)
{
    if (seconds < 0)
        seconds = 0;           // 负数按 0 处理（时长没有负值）

    const qint64 hours   = seconds / 3600;
    const qint64 minutes = (seconds % 3600) / 60;
    const qint64 secs    = seconds % 60;

    if (hours > 0) {
        // 1 小时及以上 → "HH:MM:SS"
        return pad2(hours) + QLatin1Char(':')
             + pad2(minutes) + QLatin1Char(':')
             + pad2(secs);
    }
    // 不足 1 小时 → "MM:SS"
    return pad2(minutes) + QLatin1Char(':') + pad2(secs);
}

} // namespace utils
} // namespace datascope
