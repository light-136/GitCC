// ============================================================
// 文件：IFileService.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义文件系统操作服务接口（读写/复制/移动/删除/压缩/监控）
// 设计思路：
//   IFileService 将文件系统操作封装为可注入的服务接口，
//   解耦业务逻辑与 System.IO 直接调用，带来以下好处：
//     1. 可测试性：单元测试可 Mock 文件系统，不依赖真实磁盘
//     2. 可替换性：可切换为 FTP、云存储、内存文件系统等实现
//     3. 审计能力：实现类可自动发布 FileOperationEvent 领域事件
//   文件监控（WatchDirectory）用于监听配方文件、日志目录的变化，
//   实现文件热加载和实时日志尾随功能。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 文件系统操作服务接口。
    /// 提供文本/二进制文件的完整读写操作和目录监控能力。
    /// </summary>
    public interface IFileService
    {
        // ----------------------------------------------------------------
        // 文本文件操作
        // ----------------------------------------------------------------

        /// <summary>
        /// 异步读取文本文件内容。
        /// </summary>
        /// <param name="filePath">文件绝对路径</param>
        /// <param name="encoding">文件编码（null 时自动检测 BOM，默认 UTF-8）</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<string> ReadTextAsync(
            string filePath,
            string? encoding = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步写入文本文件（覆盖写入，目标目录不存在时自动创建）。
        /// </summary>
        /// <param name="filePath">文件绝对路径</param>
        /// <param name="content">要写入的文本内容</param>
        /// <param name="encoding">文件编码（默认 UTF-8 with BOM）</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task WriteTextAsync(
            string filePath,
            string content,
            string? encoding = null,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 二进制文件操作
        // ----------------------------------------------------------------

        /// <summary>
        /// 异步读取文件的全部字节内容。
        /// </summary>
        Task<byte[]> ReadBytesAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步写入字节数据到文件（覆盖写入）。
        /// </summary>
        Task WriteBytesAsync(string filePath, byte[] data, CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 文件管理操作
        // ----------------------------------------------------------------

        /// <summary>
        /// 异步复制文件（目标目录不存在时自动创建）。
        /// </summary>
        /// <param name="sourcePath">源文件路径</param>
        /// <param name="destinationPath">目标文件路径</param>
        /// <param name="overwrite">目标文件已存在时是否覆盖（默认 false）</param>
        Task CopyAsync(string sourcePath, string destinationPath, bool overwrite = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步移动文件（跨磁盘时先复制后删除）。
        /// </summary>
        Task MoveAsync(string sourcePath, string destinationPath, bool overwrite = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步删除文件（文件不存在时不抛异常，幂等操作）。
        /// </summary>
        Task DeleteAsync(string filePath, CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 压缩/解压
        // ----------------------------------------------------------------

        /// <summary>
        /// 异步压缩文件或目录（生成 ZIP 格式压缩包）。
        /// </summary>
        /// <param name="sourcePath">要压缩的文件或目录路径</param>
        /// <param name="zipFilePath">生成的 ZIP 文件路径</param>
        Task CompressAsync(string sourcePath, string zipFilePath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 异步解压 ZIP 文件到指定目录。
        /// </summary>
        /// <param name="zipFilePath">ZIP 文件路径</param>
        /// <param name="destinationDirectory">解压目标目录</param>
        /// <param name="overwrite">文件已存在时是否覆盖（默认 true）</param>
        Task DecompressAsync(string zipFilePath, string destinationDirectory, bool overwrite = true,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 文件系统查询
        // ----------------------------------------------------------------

        /// <summary>
        /// 获取文件信息（大小、创建时间、修改时间）。
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>文件信息元组（文件不存在时返回 null）</returns>
        (long Size, DateTime CreatedAt, DateTime ModifiedAt)? GetFileInfo(string filePath);

        /// <summary>
        /// 异步列出目录中的文件列表。
        /// </summary>
        /// <param name="directoryPath">目录路径</param>
        /// <param name="searchPattern">搜索模式（如 "*.log"、"recipe_*.json"，默认 "*"）</param>
        /// <param name="recursive">是否递归子目录（默认 false）</param>
        Task<IReadOnlyList<string>> ListFilesAsync(
            string directoryPath,
            string searchPattern = "*",
            bool recursive = false,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 目录监控（文件热加载、日志实时尾随）
        // ----------------------------------------------------------------

        /// <summary>
        /// 监控目录中的文件变化（创建/修改/删除/重命名）。
        /// </summary>
        /// <param name="directoryPath">要监控的目录路径</param>
        /// <param name="filter">文件过滤器（如 "*.json"）</param>
        /// <param name="onChanged">文件变化时的回调（参数：变化类型, 文件路径）</param>
        /// <returns>监控令牌（Dispose 时停止监控）</returns>
        IDisposable WatchDirectory(
            string directoryPath,
            string filter,
            Action<string, string> onChanged);
    }
}
