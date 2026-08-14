// ============================================================
//  固件文件解析服务实现
// ============================================================
#include "FirmwareParser.h"
#include <QFile>
#include <QFileInfo>
#include <QDebug>

bool FirmwareParser::loadFirmware(const QString &filePath, FirmwareInfo *info, QString *error)
{
    if (error) error->clear();

    // ---- 1. 文件存在性检查 ----
    if (!QFile::exists(filePath))
    {
        if (error) *error = QStringLiteral("固件文件不存在: %1").arg(filePath);
        return false;
    }

    QFileInfo fileInfo(filePath);

    // ---- 2. 扩展名校验（仅支持 .bin） ----
    if (fileInfo.suffix().toLower() != QStringLiteral("bin"))
    {
        if (error) *error = QStringLiteral("不支持的文件格式: .%1，仅支持 .bin 格式").arg(fileInfo.suffix());
        return false;
    }

    // ---- 3. 大小校验 ----
    qint64 size = fileInfo.size();
    if (size == 0)
    {
        if (error) *error = QStringLiteral("固件文件为空");
        return false;
    }
    if (size > MAX_FILE_SIZE)
    {
        if (error) *error = QStringLiteral("固件文件过大: %1 字节，最大支持 %2 KB")
                                .arg(size).arg(MAX_FILE_SIZE / 1024);
        return false;
    }

    // ---- 4. 读取文件内容 ----
    QFile file(filePath);
    if (!file.open(QIODevice::ReadOnly))
    {
        if (error) *error = QStringLiteral("无法读取文件: %1").arg(file.errorString());
        return false;
    }

    QByteArray data = file.readAll();
    file.close();

    // ---- 5. 填充结果 ----
    if (info)
    {
        info->filePath = fileInfo.absoluteFilePath();
        info->fileName = fileInfo.fileName();
        info->fileSize = size;
        info->data = data;
    }
    return true;
}
