// ============================================================
// 文件：FileService.cs
// 层次：基础设施层 (Infrastructure Layer) — 文件系统服务
// 职责：
//   实现 SmartIndustry.Domain.Interfaces.IFileService 接口，提供安全的
//   文件读写、复制移动删除、ZIP 压缩解压、目录监视功能。
// 设计思路：
//   工业平台文件操作覆盖：配方存储、视觉图像归档、日志导出、报告生成。
//   安全设计：
//     1. 路径规范化防目录遍历攻击
//     2. 删除操作限制在安全根目录内
//   WatchDirectory 封装 FileSystemWatcher 为 IDisposable 令牌模式，
//   （Domain 层接口签名为回调 + IDisposable 返回值，非 IObservable）。
//   所有操作完成后发布 FileOperationEvent 到事件总线（审计用途）。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Events;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Enums;
using System.IO.Compression;
using System.Text;

namespace SmartIndustry.Infrastructure.FileSystem
{
    /// <summary>
    /// 文件系统服务实现，实现 Domain 层 IFileService 接口。
    /// 提供工业平台所有文件操作能力，含路径安全验证和操作事件发布。
    /// </summary>
    public class FileService : IFileService
    {
        // ----------------------------------------------------------------
        // 依赖注入字段
        // ----------------------------------------------------------------

        /// <summary>事件总线：文件操作完成后发布 FileOperationEvent</summary>
        private readonly IEventBus _eventBus;

        /// <summary>
        /// 安全根目录：所有删除操作只允许在此目录内执行，null=不限制
        /// </summary>
        private readonly string? _safeRootDirectory;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="eventBus">事件总线</param>
        /// <param name="safeRootDirectory">删除操作的安全边界目录，null=不限制</param>
        public FileService(IEventBus eventBus, string? safeRootDirectory = null)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _safeRootDirectory = safeRootDirectory != null
                ? Path.GetFullPath(safeRootDirectory)
                : null;
        }

        // ================================================================
        // IFileService 文本读写实现（匹配 Domain 层接口签名）
        // ================================================================

        /// <summary>
        /// 异步读取文本文件内容（支持 encoding 参数指定编码，null=默认 UTF-8）
        /// </summary>
        public async Task<string> ReadTextAsync(string filePath, string? encoding = null,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(filePath);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件不存在：{filePath}");

            // 根据 encoding 字符串名称获取编码对象（null=UTF-8）
            var enc = encoding != null ? Encoding.GetEncoding(encoding) : Encoding.UTF8;
            var content = await File.ReadAllTextAsync(filePath, enc, cancellationToken);

            await PublishFileEventAsync(FileOperationType.Read, filePath, true);
            return content;
        }

        /// <summary>
        /// 异步写入文本文件（覆盖写入，目标目录不存在时自动创建）
        /// </summary>
        public async Task WriteTextAsync(string filePath, string content, string? encoding = null,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(filePath);
            EnsureDirectoryExists(Path.GetDirectoryName(filePath));

            var enc = encoding != null ? Encoding.GetEncoding(encoding) : Encoding.UTF8;
            await File.WriteAllTextAsync(filePath, content, enc, cancellationToken);
            await PublishFileEventAsync(FileOperationType.Write, filePath, true);
        }

        // ================================================================
        // IFileService 二进制读写实现
        // ================================================================

        public async Task<byte[]> ReadBytesAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ValidatePath(filePath);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"文件不存在：{filePath}");

            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            await PublishFileEventAsync(FileOperationType.Read, filePath, true);
            return bytes;
        }

        public async Task WriteBytesAsync(string filePath, byte[] data, CancellationToken cancellationToken = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            ValidatePath(filePath);
            EnsureDirectoryExists(Path.GetDirectoryName(filePath));

            await File.WriteAllBytesAsync(filePath, data, cancellationToken);
            await PublishFileEventAsync(FileOperationType.Write, filePath, true);
        }

        // ================================================================
        // IFileService 文件操作实现
        // ================================================================

        public async Task CopyAsync(string sourcePath, string destinationPath, bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(sourcePath);
            ValidatePath(destinationPath);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"源文件不存在：{sourcePath}");
            if (!overwrite && File.Exists(destinationPath))
                throw new IOException($"目标文件已存在且不允许覆盖：{destinationPath}");

            EnsureDirectoryExists(Path.GetDirectoryName(destinationPath));

            await using var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 81920, true);
            await using var dst = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, true);
            await src.CopyToAsync(dst, cancellationToken);

            await PublishFileEventAsync(FileOperationType.Copy, sourcePath, true);
        }

        public async Task MoveAsync(string sourcePath, string destinationPath, bool overwrite = false,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(sourcePath);
            ValidatePath(destinationPath);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"源文件不存在：{sourcePath}");

            EnsureDirectoryExists(Path.GetDirectoryName(destinationPath));
            File.Move(sourcePath, destinationPath, overwrite);

            await PublishFileEventAsync(FileOperationType.Move, sourcePath, true);
        }

        public async Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ValidatePath(filePath);
            ValidateDeletePermission(filePath);

            if (File.Exists(filePath))
                File.Delete(filePath);

            await PublishFileEventAsync(FileOperationType.Delete, filePath, true);
        }

        // ================================================================
        // IFileService 压缩解压实现
        // ================================================================

        public async Task CompressAsync(string sourcePath, string zipFilePath,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(sourcePath);
            ValidatePath(zipFilePath);
            EnsureDirectoryExists(Path.GetDirectoryName(zipFilePath));

            await Task.Run(() =>
            {
                if (Directory.Exists(sourcePath))
                    ZipFile.CreateFromDirectory(sourcePath, zipFilePath,
                        CompressionLevel.Optimal, includeBaseDirectory: true);
                else if (File.Exists(sourcePath))
                {
                    using var archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);
                    archive.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath),
                        CompressionLevel.Optimal);
                }
                else
                    throw new FileNotFoundException($"压缩源路径不存在：{sourcePath}");
            }, cancellationToken);

            await PublishFileEventAsync(FileOperationType.Compress, sourcePath, true);
        }

        public async Task DecompressAsync(string zipFilePath, string destinationDirectory,
            bool overwrite = true, CancellationToken cancellationToken = default)
        {
            ValidatePath(zipFilePath);
            ValidatePath(destinationDirectory);
            if (!File.Exists(zipFilePath))
                throw new FileNotFoundException($"ZIP 文件不存在：{zipFilePath}");

            EnsureDirectoryExists(destinationDirectory);

            await Task.Run(() =>
                ZipFile.ExtractToDirectory(zipFilePath, destinationDirectory, overwriteFiles: overwrite),
                cancellationToken);

            await PublishFileEventAsync(FileOperationType.Decompress, zipFilePath, true);
        }

        // ================================================================
        // IFileService 文件系统查询实现
        // ================================================================

        /// <summary>
        /// 获取文件信息（大小、创建时间、修改时间）。
        /// 匹配 Domain 层接口签名：返回元组而非 DTO。
        /// </summary>
        public (long Size, DateTime CreatedAt, DateTime ModifiedAt)? GetFileInfo(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            var info = new FileInfo(filePath);
            if (!info.Exists) return null;
            return (info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc);
        }

        /// <summary>
        /// 列出目录中的文件列表（返回文件路径字符串，匹配 Domain 层接口签名）
        /// </summary>
        public async Task<IReadOnlyList<string>> ListFilesAsync(
            string directoryPath,
            string searchPattern = "*",
            bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(directoryPath);
            if (!Directory.Exists(directoryPath)) return Array.Empty<string>();

            return await Task.Run(() =>
            {
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                return Directory.GetFiles(directoryPath, searchPattern, option) as IReadOnlyList<string>;
            }, cancellationToken);
        }

        // ================================================================
        // IFileService 目录监控实现（回调 + IDisposable 模式）
        // ================================================================

        /// <summary>
        /// 监控目录文件变更，通过回调 Action 通知（匹配 Domain 层接口签名）。
        /// onChanged 参数：(变化类型字符串, 文件路径)
        /// 返回 IDisposable 令牌，Dispose 时停止监控并释放 FileSystemWatcher。
        /// </summary>
        public IDisposable WatchDirectory(string directoryPath, string filter,
            Action<string, string> onChanged)
        {
            ValidatePath(directoryPath);
            if (!Directory.Exists(directoryPath))
                throw new DirectoryNotFoundException($"监视目录不存在：{directoryPath}");

            var watcher = new FileSystemWatcher(directoryPath, filter)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            // 将 FileSystemWatcher 事件桥接为回调
            watcher.Changed += (s, e) => SafeInvoke(onChanged, "Changed", e.FullPath);
            watcher.Created += (s, e) => SafeInvoke(onChanged, "Created", e.FullPath);
            watcher.Deleted += (s, e) => SafeInvoke(onChanged, "Deleted", e.FullPath);
            watcher.Renamed += (s, e) => SafeInvoke(onChanged, "Renamed", e.FullPath);

            // 返回包装了 Dispose 逻辑的令牌
            return new WatcherDisposable(watcher);
        }

        // ================================================================
        // 私有工具
        // ================================================================

        private static void ValidatePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("路径不能为空");
            var invalidChars = Path.GetInvalidPathChars();
            if (path.Any(c => invalidChars.Contains(c)))
                throw new ArgumentException($"路径包含非法字符：{path}");
        }

        private void ValidateDeletePermission(string filePath)
        {
            if (_safeRootDirectory == null) return;
            var fullPath = Path.GetFullPath(filePath);
            if (!fullPath.StartsWith(_safeRootDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                && !fullPath.Equals(_safeRootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    $"拒绝删除操作：'{fullPath}' 不在安全目录 '{_safeRootDirectory}' 内");
            }
        }

        private static void EnsureDirectoryExists(string? dirPath)
        {
            if (!string.IsNullOrWhiteSpace(dirPath) && !Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);
        }

        private async Task PublishFileEventAsync(FileOperationType type, string path, bool success)
        {
            try { await _eventBus.PublishAsync(new FileOperationEvent(type, path, success)); }
            catch { /* 事件总线失败不影响文件操作 */ }
        }

        private static void SafeInvoke(Action<string, string> callback, string changeType, string filePath)
        {
            try { callback(changeType, filePath); }
            catch { /* 回调异常隔离，不影响监视器继续工作 */ }
        }

        // ================================================================
        // 内部 Disposable 包装（管理 FileSystemWatcher 生命周期）
        // ================================================================

        private sealed class WatcherDisposable : IDisposable
        {
            private readonly FileSystemWatcher _watcher;
            private bool _disposed;

            public WatcherDisposable(FileSystemWatcher watcher) => _watcher = watcher;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
        }
    }
}
