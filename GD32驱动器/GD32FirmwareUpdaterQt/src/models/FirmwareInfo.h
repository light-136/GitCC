// ============================================================
//  固件文件信息模型
//  存储加载的固件文件相关信息（与 WPF 版 FirmwareInfo.cs 对应）
// ============================================================
#ifndef FIRMWAREINFO_H
#define FIRMWAREINFO_H

#include <QString>
#include <QByteArray>

struct FirmwareInfo
{
    // 文件完整路径
    QString filePath;

    // 文件名（不含路径）
    QString fileName;

    // 固件数据（二进制内容）
    QByteArray data;

    // 文件大小（字节）
    qint64 fileSize = 0;

    /// <summary>
    /// 文件大小的友好显示（如 "12.5 KB"）
    /// </summary>
    QString fileSizeText() const
    {
        if (fileSize < 1024)       return QString::number(fileSize) + " B";
        if (fileSize < 1024 * 1024) return QString::number(fileSize / 1024.0, 'f', 1) + " KB";
        return QString::number(fileSize / (1024.0 * 1024.0), 'f', 2) + " MB";
    }
};

#endif // FIRMWAREINFO_H
