using SmartMES.Core.Infrastructure;
using SmartMES.Modules.Database;
using SmartMES.Modules.FileProcess;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartMES.UI.Modules.DatabaseModule
{
    public class DatabaseViewModel : ViewModelBase
    {
        private SmartMesDbContext? _ctx;
        private EfRepository<ProductionRecord>? _repo;
        private readonly InMemoryKvStore _kv = new();
        private string _dbType = "SQLite";
        private string _statusText = "未连接 - 点击“连接”后自动建表";

        public ObservableCollection<string> DbTypes { get; } = new() { "SQLite", "MySQL", "SQL Server" };
        public string SelectedDbType { get => _dbType; set => SetProperty(ref _dbType, value); }
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        public ObservableCollection<ProductionRecord> Records { get; } = new();
        public ObservableCollection<string> KvItems { get; } = new();
        public ObservableCollection<string> Logs { get; } = new();
        public ObservableCollection<string> StatsRows { get; } = new();

        private ProductionRecord? _selectedRecord;
        public ProductionRecord? SelectedRecord
        {
            get => _selectedRecord;
            set
            {
                SetProperty(ref _selectedRecord, value);
                DeleteRecordCommand.RaiseCanExecuteChanged();
            }
        }

        public RelayCommand ConnectDbCommand { get; }
        public RelayCommand LoadRecordsCommand { get; }
        public RelayCommand AddRecordCommand { get; }
        public RelayCommand DeleteRecordCommand { get; }
        public RelayCommand SeedDataCommand { get; }
        public RelayCommand QueryStatsCommand { get; }
        public RelayCommand ExportCsvCommand { get; }
        public RelayCommand ExportJsonCommand { get; }
        public RelayCommand ExportExcelCommand { get; }
        public RelayCommand KvSetCommand { get; }
        public RelayCommand KvRefreshCommand { get; }

        public DatabaseViewModel()
        {
            ConnectDbCommand = new RelayCommand(async _ => await ConnectAsync());
            LoadRecordsCommand = new RelayCommand(async _ => await LoadAsync(), _ => _ctx != null);
            AddRecordCommand = new RelayCommand(async _ => await AddRecordAsync(), _ => _repo != null);
            DeleteRecordCommand = new RelayCommand(async _ => await DeleteAsync(), _ => _selectedRecord != null && _repo != null);
            SeedDataCommand = new RelayCommand(async _ => await SeedAsync(), _ => _repo != null);
            QueryStatsCommand = new RelayCommand(async _ => await QueryStatsAsync(), _ => _ctx != null);
            ExportCsvCommand = new RelayCommand(async _ => await ExportAsync("csv"), _ => Records.Count > 0);
            ExportJsonCommand = new RelayCommand(async _ => await ExportAsync("json"), _ => Records.Count > 0);
            ExportExcelCommand = new RelayCommand(async _ => await ExportAsync("xlsx"), _ => Records.Count > 0);
            KvSetCommand = new RelayCommand(_ => KvSet());
            KvRefreshCommand = new RelayCommand(_ => KvRefresh());
        }

        private void RaiseAllCanExecute()
        {
            LoadRecordsCommand.RaiseCanExecuteChanged();
            AddRecordCommand.RaiseCanExecuteChanged();
            DeleteRecordCommand.RaiseCanExecuteChanged();
            SeedDataCommand.RaiseCanExecuteChanged();
            QueryStatsCommand.RaiseCanExecuteChanged();
            ExportCsvCommand.RaiseCanExecuteChanged();
            ExportJsonCommand.RaiseCanExecuteChanged();
            ExportExcelCommand.RaiseCanExecuteChanged();
        }

        private async Task ConnectAsync()
        {
            try
            {
                _ctx?.Dispose();
                var dbEnum = _dbType == "SQLite" ? DbType.SQLite
                    : _dbType == "MySQL" ? DbType.MySQL
                    : DbType.SqlServer;

                string conn = dbEnum switch
                {
                    DbType.SQLite => "Data Source=smartmes.db",
                    DbType.MySQL => "Server=localhost;Port=3306;Database=smartmes;User=root;Password=123456;",
                    DbType.SqlServer => "Server=(localdb)\\mssqllocaldb;Database=SmartMES;Trusted_Connection=True;",
                    _ => "Data Source=smartmes.db"
                };

                _ctx = DbContextFactory.Create(dbEnum, conn);
                await _ctx.Database.EnsureCreatedAsync();
                _repo = new EfRepository<ProductionRecord>(_ctx);

                StatusText = $"{_dbType} 已连接 ✔";
                AddLog($"已连接 {_dbType}");
                RaiseAllCanExecute();

                var existing = await _repo.GetAllAsync();
                if (existing.Count == 0) await SeedAsync();
                else await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"连接失败: {ex.Message[..Math.Min(60, ex.Message.Length)]}";
                AddLog($"[ERR] {ex.Message}");
            }
        }

        private async Task SeedAsync()
        {
            if (_repo == null) return;

            var rnd = new Random(42);
            var products = new[] { "P001", "P002", "P003", "P004", "P005" };
            var operators = new[] { "张伟", "李明", "王芳", "刘洋", "陈静" };
            var orders = new[]
            {
                $"WO-{DateTime.Today:yyyyMMdd}-01",
                $"WO-{DateTime.Today:yyyyMMdd}-02",
                $"WO-{DateTime.Today:yyyyMMdd}-03"
            };

            for (int i = 0; i < 60; i++)
            {
                var rec = new ProductionRecord
                {
                    OrderId = orders[i % orders.Length],
                    ProductCode = products[i % products.Length],
                    Qty = rnd.Next(1, 50),
                    IsPass = rnd.NextDouble() > 0.12,
                    Temperature = Math.Round(20 + rnd.NextDouble() * 60, 1),
                    Pressure = Math.Round(1 + rnd.NextDouble() * 9, 2),
                    Operator = operators[i % operators.Length],
                    RecordTime = DateTime.Now.AddMinutes(-i * 15)
                };
                await _repo.AddAsync(rec);
            }

            AddLog("✔ 生成60条仿真记录");
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (_repo == null) return;

            var list = await _repo.GetAllAsync();
            Records.Clear();
            foreach (var r in list.OrderByDescending(x => x.RecordTime).Take(100))
                Records.Add(r);

            StatusText = $"已加载 {Records.Count} 条（共 {list.Count} 条）";
            AddLog($"加载 {list.Count} 条");

            ExportCsvCommand.RaiseCanExecuteChanged();
            ExportJsonCommand.RaiseCanExecuteChanged();
            ExportExcelCommand.RaiseCanExecuteChanged();
        }

        private async Task AddRecordAsync()
        {
            if (_repo == null) return;

            var rnd = new Random();
            var rec = new ProductionRecord
            {
                OrderId = $"WO-{DateTime.Now:HHmmss}",
                ProductCode = $"P{rnd.Next(1, 6):D3}",
                Qty = rnd.Next(1, 100),
                IsPass = rnd.NextDouble() > 0.1,
                Temperature = Math.Round(20 + rnd.NextDouble() * 60, 1),
                Pressure = Math.Round(1 + rnd.NextDouble() * 9, 2),
                Operator = "手动"
            };

            await _repo.AddAsync(rec);
            await LoadAsync();
            AddLog($"新增 ID={rec.Id}");
        }

        private async Task DeleteAsync()
        {
            if (_repo == null || _selectedRecord == null) return;

            var id = _selectedRecord.Id;
            await _repo.DeleteAsync(id);
            await LoadAsync();
            AddLog($"删除 ID={id}");
        }

        private async Task QueryStatsAsync()
        {
            if (_repo == null) return;

            var all = await _repo.GetAllAsync();
            StatsRows.Clear();

            var grouped = all.GroupBy(r => r.OrderId).Select(g => new
            {
                OrderId = g.Key,
                Total = g.Count(),
                Pass = g.Count(x => x.IsPass),
                PassRate = Math.Round(100.0 * g.Count(x => x.IsPass) / g.Count(), 1),
                AvgTemp = Math.Round(g.Average(x => x.Temperature), 1)
            }).OrderByDescending(x => x.PassRate).ToList();

            StatsRows.Add("工单号                | 总数 | 合格 | 良品率 | 平均温度");
            StatsRows.Add(new string('-', 60));
            foreach (var s in grouped)
                StatsRows.Add($"{s.OrderId,-20} | {s.Total,4} | {s.Pass,4} | {s.PassRate,6:F1}% | {s.AvgTemp,6:F1}℃");

            AddLog($"统计完成，共 {grouped.Count} 个工单");
        }

        private async Task ExportAsync(string fmt)
        {
            if (Records.Count == 0) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"db_{DateTime.Now:yyyyMMdd_HHmmss}.{fmt}",
                Filter = fmt == "xlsx" ? "Excel|*.xlsx" : fmt == "json" ? "JSON|*.json" : "CSV|*.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var headers = new[] { "ID", "工单", "产品", "数量", "合格", "温度", "压力", "时间", "操作员" };
            var rows = Records.Select(r => new[]
            {
                r.Id.ToString(),
                r.OrderId,
                r.ProductCode,
                r.Qty.ToString(),
                r.IsPass ? "合格" : "不合格",
                r.Temperature.ToString("F1"),
                r.Pressure.ToString("F2"),
                r.RecordTime.ToString("yyyy-MM-dd HH:mm"),
                r.Operator
            }).ToList();

            IFileService svc = fmt switch
            {
                "xlsx" => new ExcelFileService(),
                "json" => new JsonFileService(),
                _ => new CsvFileService()
            };

            var ok = await svc.WriteAsync(dlg.FileName, rows, headers);
            AddLog(ok
                ? $"✔ 导出{fmt.ToUpper()}: {System.IO.Path.GetFileName(dlg.FileName)}"
                : "✘ 导出失败");
        }

        private void KvSet()
        {
            var rnd = new Random();
            _kv.Set($"sensor:temp:{rnd.Next(1, 5)}", Math.Round(20 + rnd.NextDouble() * 60, 1), TimeSpan.FromSeconds(60));
            _kv.Set($"sensor:pressure:{rnd.Next(1, 5)}", Math.Round(1 + rnd.NextDouble() * 9, 2), TimeSpan.FromSeconds(60));
            KvRefresh();
        }

        private void KvRefresh()
        {
            KvItems.Clear();
            foreach (var k in _kv.Keys()) KvItems.Add($"{k} = {_kv.Get<object>(k)}");
        }

        private void AddLog(string msg)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
                if (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
            });
        }
    }
}
