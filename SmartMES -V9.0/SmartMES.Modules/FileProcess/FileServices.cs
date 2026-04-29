using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace SmartMES.Modules.FileProcess
{
    // ============================================================
    // 文件处理模块
    // 支持：TXT / CSV / Excel(.xlsx) / XML / JSON
    // 统一接口设计，上层代码无需关心具体格式
    // ============================================================

    /// <summary>文件操作结果</summary>
    public class FileResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string[]> Rows { get; set; } = new(); // 表格行，每行是字段数组
        public string RawText { get; set; } = string.Empty;
    }

    /// <summary>文件服务接口</summary>
    public interface IFileService
    {
        string SupportedExtensions { get; }
        Task<FileResult> ReadAsync(string filePath);
        Task<bool> WriteAsync(string filePath, List<string[]> rows, string[]? headers = null);
    }

    // ======================== TXT 处理 ========================
    public class TxtFileService : IFileService
    {
        public string SupportedExtensions => ".txt";

        /// <summary>
        /// 自动补齐：ReadAsync 方法说明。
        /// </summary>
        public async Task<FileResult> ReadAsync(string filePath)
        {
            var text = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var lines = text.Split(Environment.NewLine);
            return new FileResult
            {
                Success = true,
                RawText = text,
                Rows = lines.Select(l => new[] { l }).ToList()
            };
        }

        /// <summary>
        /// 自动补齐：WriteAsync 方法说明。
        /// </summary>
        public async Task<bool> WriteAsync(string filePath, List<string[]> rows, string[]? headers = null)
        {
            var lines = rows.Select(r => string.Join(" ", r));
            await File.WriteAllLinesAsync(filePath, lines, Encoding.UTF8);
            return true;
        }
    }

    // ======================== CSV 处理 ========================
    public class CsvFileService : IFileService
    {
        public string SupportedExtensions => ".csv";
        private readonly char _delimiter;

        /// <summary>
        /// 自动补齐：CsvFileService 方法说明。
        /// </summary>
        public CsvFileService(char delimiter = ',')
        {
            _delimiter = delimiter;
        }

        /// <summary>
        /// 自动补齐：ReadAsync 方法说明。
        /// </summary>
        public async Task<FileResult> ReadAsync(string filePath)
        {
            var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
            var rows = lines.Select(l => l.Split(_delimiter)).ToList();
            return new FileResult { Success = true, Rows = rows, RawText = string.Join("\n", lines) };
        }

        /// <summary>
        /// 自动补齐：WriteAsync 方法说明。
        /// </summary>
        public async Task<bool> WriteAsync(string filePath, List<string[]> rows, string[]? headers = null)
        {
            var sb = new StringBuilder();
            if (headers != null)
                sb.AppendLine(string.Join(_delimiter, headers));
            foreach (var row in rows)
                sb.AppendLine(string.Join(_delimiter, row));
            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
            return true;
        }
    }

    // ======================== JSON 处理 ========================
    public class JsonFileService : IFileService
    {
        public string SupportedExtensions => ".json";
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        /// <summary>
        /// 自动补齐：ReadAsync 方法说明。
        /// </summary>
        public async Task<FileResult> ReadAsync(string filePath)
        {
            var text = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            try
            {
                using var doc = JsonDocument.Parse(text);
                var rows = new List<string[]>();
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        if (elem.ValueKind == JsonValueKind.Object)
                            rows.Add(elem.EnumerateObject()
                                .Select(p => $"{p.Name}={p.Value}").ToArray());
                        else
                            rows.Add(new[] { elem.ToString() });
                    }
                }
                return new FileResult { Success = true, RawText = text, Rows = rows };
            }
            catch (Exception ex)
            {
                return new FileResult { Success = false, Message = ex.Message, RawText = text };
            }
        }

        /// <summary>
        /// 自动补齐：WriteAsync 方法说明。
        /// </summary>
        public async Task<bool> WriteAsync(string filePath, List<string[]> rows, string[]? headers = null)
        {
            var list = rows.Select(r =>
            {
                if (headers != null && headers.Length == r.Length)
                    return (object)headers.Zip(r, (h, v) => new { h, v })
                        .ToDictionary(x => x.h, x => x.v);
                return (object)r;
            }).ToList();
            var json = JsonSerializer.Serialize(list, _opts);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
            return true;
        }
    }

    // ======================== XML 处理 ========================
    public class XmlFileService : IFileService
    {
        public string SupportedExtensions => ".xml";

        /// <summary>
        /// 自动补齐：ReadAsync 方法说明。
        /// </summary>
        public async Task<FileResult> ReadAsync(string filePath)
        {
            var text = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            try
            {
                var doc = XDocument.Parse(text);
                var rows = doc.Descendants()
                    .Where(e => !e.HasElements)
                    .Select(e => new[] { e.Name.LocalName, e.Value })
                    .ToList();
                return new FileResult { Success = true, RawText = text, Rows = rows };
            }
            catch (Exception ex)
            {
                return new FileResult { Success = false, Message = ex.Message, RawText = text };
            }
        }

        /// <summary>
        /// 自动补齐：WriteAsync 方法说明。
        /// </summary>
        public async Task<bool> WriteAsync(string filePath, List<string[]> rows, string[]? headers = null)
        {
            var root = new XElement("Root",
                rows.Select((r, i) => new XElement("Row",
                    new XAttribute("index", i),
                    r.Select((v, j) => new XElement(headers?[j] ?? $"Col{j}", v)))));
            await File.WriteAllTextAsync(filePath, new XDocument(root).ToString(), Encoding.UTF8);
            return true;
        }
    }
}
