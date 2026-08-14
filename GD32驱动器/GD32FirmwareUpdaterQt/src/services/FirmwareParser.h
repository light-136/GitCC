// ============================================================
//  固件文件解析服务
//  支持加载 .bin 格式的固件文件（与 WPF 版 FirmwareFileParser.cs 对应）
//
//  【Qt知识点】QFile + QFileInfo：
//  QFileInfo 类似 C# 的 FileInfo（文件名/扩展名/大小查询）；
//  错误信息通过 QString* 输出参数回传（C++ 常用手法，
//  等价 C# 的 throw + catch，但更轻量）。
// ============================================================
#ifndef FIRMWAREPARSER_H
#define FIRMWAREPARSER_H

#include <QString>
#include "models/FirmwareInfo.h"

class FirmwareParser
{
public:
    FirmwareParser() = delete;

    /// <summary>
    /// 加载固件文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="info">输出：固件信息</param>
    /// <param name="error">输出：失败原因（成功时为空）</param>
    /// <returns>是否加载成功</returns>
    static bool loadFirmware(const QString &filePath, FirmwareInfo *info, QString *error);

private:
    // 支持的固件文件最大大小（2MB，GD32 Flash 一般不超过此值）
    static const qint64 MAX_FILE_SIZE = 2 * 1024 * 1024;
};

#endif // FIRMWAREPARSER_H
