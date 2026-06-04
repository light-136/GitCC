// 固件文件信息模型
// 存储加载的固件文件相关信息

namespace GD32FirmwareUpdater.Models
{
    /// <summary>
    /// 固件文件信息
    /// </summary>
    public class FirmwareInfo
    {
        // 文件完整路径
        public string FilePath { get; set; } = string.Empty;

        // 文件名（不含路径）
        public string FileName { get; set; } = string.Empty;

        // 固件数据（二进制内容）
        public byte[] Data { get; set; } = Array.Empty<byte>();

        // 文件大小（字节）
        public long FileSize => Data.Length;

        // 文件大小的友好显示（如 "12.5 KB"）
        public string FileSizeText
        {
            get
            {
                if (FileSize < 1024)
                    return $"{FileSize} B";
                if (FileSize < 1024 * 1024)
                    return $"{FileSize / 1024.0:F1} KB";
                return $"{FileSize / (1024.0 * 1024.0):F2} MB";
            }
        }
    }
}
