using SmartMES.Core.Infrastructure;
using SmartMES.Modules.FileProcess;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Data;
using System.Text;

namespace SmartMES.UI.Modules.FileModule
{
    public class FileViewModel : ViewModelBase
    {
        private string _selectedFormat = "CSV";
        private string _statusText = "就绪 - 请选择格式后导入或导出";
        private List<string[]> _lastRows = new();

        public ObservableCollection<string> Formats { get; } =
            new() { "TXT", "CSV", "JSON", "XML", "Excel" };

        public string SelectedFormat
        {
            get => _selectedFormat;
            set => SetProperty(ref _selectedFormat, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _previewInfo = string.Empty;
        public string PreviewInfo { get => _previewInfo; set => SetProperty(ref _previewInfo, value); }

        private DataTable _previewTable = new();
        public DataTable PreviewTable { get => _previewTable; set => SetProperty(ref _previewTable, value); }

        public RelayCommand ImportCommand { get; }
        public RelayCommand ExportCommand { get; }
        public RelayCommand ExportAllCommand { get; }

        private static readonly string[] Headers =
        {
            "工单号", "产品编码", "产品名称", "计划数量", "实际数量",
            "合格品", "不合格品", "良品率", "操作员", "生产时间"
        };

        public FileViewModel()
        {
            ImportCommand = new RelayCommand(async _ => await ImportAsync());
            ExportCommand = new RelayCommand(async _ => await ExportAsync());
            ExportAllCommand = new RelayCommand(async _ => await ExportAllAsync());
        }

        private static List<string[]> GenerateSampleRows()
        {
            var rnd = new Random(42);
            var products = new[]
            {
                ("P001", "精密齿轮"), ("P002", "轴承座"), ("P003", "连接法兰"), ("P004", "导轨滑块"), ("P005", "同步带轮")
            };
            var operators = new[] { "张伟", "李明", "王芳", "刘洋", "陈静" };

            return Enumerable.Range(1, 30).Select(i =>
            {
                var (code, name) = products[i % products.Length];
                int plan = rnd.Next(50, 200);
                int actual = rnd.Next((int)(plan * 0.85), plan + 5);
                int pass = rnd.Next((int)(actual * 0.88), actual + 1);
                int fail = actual - pass;
                double rate = Math.Round(100.0 * pass / actual, 1);
                return new[]
                {
                    $"WO-{2024000 + i}", code, name,
                    plan.ToString(), actual.ToString(),
                    pass.ToString(), fail.ToString(),
                    $"{rate:F1}",
                    operators[i % operators.Length],
                    DateTime.Now.AddHours(-i * 2).ToString("yyyy-MM-dd HH:mm")
                };
            }).ToList();
        }

        private IFileService GetService() => _selectedFormat switch
        {
            "TXT" => new TxtFileService(),
            "JSON" => new JsonFileService(),
            "XML" => new XmlFileService(),
            "Excel" => new ExcelFileService(),
            _ => new CsvFileService()
        };

        private async Task ImportAsync()
        {
            var dlg = new OpenFileDialog { Filter = GetFilter(), Title = "选择导入文件" };
            if (dlg.ShowDialog() != true) return;

            StatusText = $"读取中: {System.IO.Path.GetFileName(dlg.FileName)}";
            var result = await GetService().ReadAsync(dlg.FileName);
            if (!result.Success)
            {
                StatusText = $"读取失败: {result.Message}";
                return;
            }

            _lastRows = result.Rows;
            BuildPreviewTable(result.Rows, null);
            StatusText = $"读取成功 [{_selectedFormat}] - {result.Rows.Count} 行";
            PreviewInfo = $"{result.Rows.Count} 行";
        }

        private async Task ExportAsync()
        {
            var ext = GetExt();
            var dlg = new SaveFileDialog
            {
                Filter = GetFilter(),
                FileName = $"生产报表_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}",
                Title = "导出文件"
            };
            if (dlg.ShowDialog() != true) return;

            var rows = GenerateSampleRows();
            StatusText = "导出中...";
            var ok = await GetService().WriteAsync(dlg.FileName, rows, Headers);
            if (ok)
            {
                BuildPreviewTable(rows, Headers);
                StatusText = $"导出成功 [{_selectedFormat}] - {rows.Count} 行 - {dlg.FileName}";
                PreviewInfo = $"{rows.Count} 行 × {Headers.Length} 列";
            }
            else
            {
                StatusText = "导出失败";
            }
        }

        private async Task ExportAllAsync()
        {
            var dlg = new SaveFileDialog
            {
                Title = "选择批量导出目录（文件名任意，取其所在目录）",
                FileName = "选择此处的文件夹",
                Filter = "所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            var folder = System.IO.Path.GetDirectoryName(dlg.FileName) ?? ".";
            var rows = GenerateSampleRows();
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var sb = new StringBuilder();

            var tasks = new[]
            {
                ("CSV",   (IFileService)new CsvFileService(),   "csv"),
                ("JSON",  new JsonFileService(),                "json"),
                ("XML",   new XmlFileService(),                 "xml"),
                ("Excel", new ExcelFileService(),               "xlsx"),
                ("TXT",   new TxtFileService(),                 "txt"),
            };

            foreach (var (fmt, svc, ext) in tasks)
            {
                var path = System.IO.Path.Combine(folder, $"生产报表_{stamp}.{ext}");
                var ok = await svc.WriteAsync(path, rows, Headers);
                sb.AppendLine($"{(ok ? "✔" : "✘")} {fmt}: {System.IO.Path.GetFileName(path)}");
            }

            StatusText = sb.ToString().TrimEnd();
        }

        private void BuildPreviewTable(List<string[]> rows, string[]? headers)
        {
            var dt = new DataTable();
            if (rows.Count == 0)
            {
                PreviewTable = dt;
                return;
            }

            int cols = rows.Max(r => r.Length);
            if (headers != null && headers.Length == cols)
            {
                foreach (var h in headers) dt.Columns.Add(h);
            }
            else
            {
                for (int c = 0; c < cols; c++) dt.Columns.Add($"列{c + 1}");
            }

            foreach (var row in rows.Take(200))
            {
                var dr = dt.NewRow();
                for (int c = 0; c < Math.Min(row.Length, cols); c++) dr[c] = row[c];
                dt.Rows.Add(dr);
            }

            PreviewTable = dt;
        }

        private string GetExt() => _selectedFormat switch
        {
            "TXT" => "txt",
            "JSON" => "json",
            "XML" => "xml",
            "Excel" => "xlsx",
            _ => "csv"
        };

        private string GetFilter() => _selectedFormat switch
        {
            "TXT" => "文本文件|*.txt|所有文件|*.*",
            "JSON" => "JSON文件|*.json|所有文件|*.*",
            "XML" => "XML文件|*.xml|所有文件|*.*",
            "Excel" => "Excel文件|*.xlsx|所有文件|*.*",
            _ => "CSV文件|*.csv|所有文件|*.*"
        };
    }
}
