using System.Data;
using System.IO;
using System.Windows.Controls;
using LaserDataFilter.Models;
using LaserDataFilter.Services;

namespace LaserDataFilter
{
    public partial class MainWindow : System.Windows.Window
    {
        private DataFilterService? _service;
        private List<SummaryRow>? _currentRows;
        private string _currentSeries = string.Empty;
        private DateTime _currentDate;

        // 路径记忆文件，存放在程序所在目录下
        private static readonly string ConfigFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_path.txt");

        public MainWindow()
        {
            InitializeComponent();
            var savedPath = LoadSavedPath();
            if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
            {
                SetDataPath(savedPath, save: false);
            }
            else
            {
                var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "量测数据");
                if (Directory.Exists(defaultPath))
                    SetDataPath(Path.GetFullPath(defaultPath));
            }
        }

        /// <summary>
        /// 从本地文件加载上次保存的数据源路径
        /// </summary>
        private static string? LoadSavedPath()
        {
            if (!File.Exists(ConfigFilePath)) return null;
            var path = File.ReadAllText(ConfigFilePath, System.Text.Encoding.UTF8).Trim();
            return string.IsNullOrEmpty(path) ? null : path;
        }

        /// <summary>
        /// 将数据源路径保存到本地文件
        /// </summary>
        private static void SavePath(string path)
        {
            File.WriteAllText(ConfigFilePath, path, System.Text.Encoding.UTF8);
        }

        public void SetDataPath(string path, bool save = true)
        {
            TxtDataPath.Text = path;
            _service = new DataFilterService(path);
            if (save) SavePath(path);
            LoadSeries();
        }

        private void LoadSeries()
        {
            if (_service == null) return;
            var seriesList = _service.GetAllSeries();
            CmbSeries.ItemsSource = seriesList;
            if (seriesList.Count > 0)
                CmbSeries.SelectedIndex = 0;
        }

        private void BtnBrowse_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择量测数据目录",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SetDataPath(dialog.SelectedPath);
        }

        private void CmbSeries_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BtnExport.IsEnabled = false;
            DgPreview.ItemsSource = null;
        }

        private void BtnFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_service == null)
            {
                System.Windows.MessageBox.Show("请先选择数据源目录", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (CmbSeries.SelectedItem == null)
            {
                System.Windows.MessageBox.Show("请选择检测规格系列", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (DpDate.SelectedDate == null)
            {
                System.Windows.MessageBox.Show("请选择筛选日期", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            _currentSeries = CmbSeries.SelectedItem.ToString()!;
            _currentDate = DpDate.SelectedDate.Value;

            var files = _service.FilterFiles(_currentSeries, _currentDate);
            if (files.Count == 0)
            {
                TxtStatus.Text = "未找到符合条件的数据文件";
                DgPreview.ItemsSource = null;
                BtnExport.IsEnabled = false;
                return;
            }

            _currentRows = _service.BuildSummary(files);
            DisplaySummary(_currentRows);
            TxtStatus.Text = $"筛选完成，共 {files.Count} 个文件，{_currentRows.Count} 条记录";
            BtnExport.IsEnabled = true;
        }

        private void DisplaySummary(List<SummaryRow> rows)
        {
            if (rows.Count == 0)
            {
                DgPreview.ItemsSource = null;
                return;
            }

            int maxValues = rows.Max(r => r.Values.Count);
            var table = new DataTable();
            table.Columns.Add("时间", typeof(string));
            for (int i = 1; i <= maxValues; i++)
                table.Columns.Add($"检测值{i}", typeof(string));
            table.Columns.Add("判定结果", typeof(string));
            table.Columns.Add("唯一标识号", typeof(string));

            foreach (var row in rows)
            {
                var dr = table.NewRow();
                dr["时间"] = row.Time;
                for (int i = 0; i < maxValues; i++)
                    dr[$"检测值{i + 1}"] = i < row.Values.Count ? row.Values[i] : "";
                dr["判定结果"] = row.Result;
                dr["唯一标识号"] = row.UniqueId;
                table.Rows.Add(dr);
            }

            DgPreview.ItemsSource = table.DefaultView;
        }

        private void BtnExport_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_service == null || _currentRows == null || _currentRows.Count == 0) return;

            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择导出目录",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            try
            {
                var outputPath = _service.ExportCsv(_currentSeries, _currentDate, _currentRows, dialog.SelectedPath);
                TxtStatus.Text = $"导出成功：{outputPath}";
                System.Windows.MessageBox.Show($"数据已导出到：\n{outputPath}", "导出成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"导出失败：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
