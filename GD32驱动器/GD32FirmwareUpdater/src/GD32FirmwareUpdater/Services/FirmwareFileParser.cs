// 固件文件解析服务
// 支持加载 .bin 格式的固件文件

using System.IO;

namespace GD32FirmwareUpdater.Services
{
    /// <summary>
    /// 固件文件解析器
    /// 负责加载和验证固件文件
    /// </summary>
    public static class FirmwareFileParser
    {
        // 支持的固件文件最大大小（2MB，GD32 Flash一般不超过此值）
        private const long MAX_FILE_SIZE = 2 * 1024 * 1024;

        /// <summary>
        /// 加载固件文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>固件信息对象</returns>
        /// <exception cref="FileNotFoundException">文件不存在</exception>
        /// <exception cref="InvalidOperationException">文件格式或大小不合法</exception>
        public static Models.FirmwareInfo LoadFirmware(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("固件文件不存在", filePath);

            var fileInfo = new FileInfo(filePath);

            // 校验文件扩展名
            string ext = fileInfo.Extension.ToLowerInvariant();
            if (ext != ".bin")
                throw new InvalidOperationException($"不支持的文件格式: {ext}，仅支持 .bin 格式");

            // 校验文件大小
            if (fileInfo.Length == 0)
                throw new InvalidOperationException("固件文件为空");

            if (fileInfo.Length > MAX_FILE_SIZE)
                throw new InvalidOperationException($"固件文件过大: {fileInfo.Length} 字节，最大支持 {MAX_FILE_SIZE / 1024} KB");

            // 读取文件内容
            byte[] data = File.ReadAllBytes(filePath);

            return new Models.FirmwareInfo
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                Data = data
            };
        }
    }
}
