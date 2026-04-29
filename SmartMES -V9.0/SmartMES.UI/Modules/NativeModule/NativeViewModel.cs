using SmartMES.Core.Infrastructure;
using SmartMES.Modules.NativeInterop;
using System.Collections.ObjectModel;
using System.Text;

namespace SmartMES.UI.Modules.NativeModule
{
    public class NativeViewModel : ViewModelBase
    {
        private string _resultText = string.Empty;
        private string _nativeVersion = string.Empty;

        public string ResultText { get => _resultText; set => SetProperty(ref _resultText, value); }
        public string NativeVersion { get => _nativeVersion; set => SetProperty(ref _nativeVersion, value); }
        public ObservableCollection<string> Logs { get; } = new();

        public RelayCommand CheckVersionCommand { get; }
        public RelayCommand StatisticsCommand { get; }
        public RelayCommand MatrixCommand { get; }
        public RelayCommand FilterCommand { get; }
        public RelayCommand StringCommand { get; }

        private readonly Random _rnd = new();

        public NativeViewModel()
        {
            CheckVersionCommand = new RelayCommand(_ => CheckVersion());
            StatisticsCommand = new RelayCommand(_ => RunStatistics());
            MatrixCommand = new RelayCommand(_ => RunMatrix());
            FilterCommand = new RelayCommand(_ => RunFilter());
            StringCommand = new RelayCommand(_ => RunString());
        }

        private void CheckVersion()
        {
            NativeVersion = NativeHelper.GetNativeVersion();
            AddLog($"Native DLL: {NativeVersion}");
            AddLog($"可用: {NativeHelper.IsNativeAvailable}");
        }

        private void RunStatistics()
        {
            var data = Enumerable.Range(0, 50)
                .Select(_ => Math.Round(_rnd.NextDouble() * 100, 2))
                .ToArray();

            var (mean, std, median, min, max) = NativeHelper.Statistics(data);
            var sb = new StringBuilder();
            sb.AppendLine($"样本: [{string.Join(", ", data.Take(8))} ...] ({data.Length}个)");
            sb.AppendLine($"均值    = {mean:F3}");
            sb.AppendLine($"标准差  = {std:F3}");
            sb.AppendLine($"中位数  = {median:F3}");
            sb.AppendLine($"最小值  = {min:F3}");
            sb.AppendLine($"最大值  = {max:F3}");
            ResultText = sb.ToString();
            AddLog("统计计算完成（C++ Native）");
        }

        private void RunMatrix()
        {
            var a = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var b = new double[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 };
            var c = NativeHelper.MatrixMultiply(a, b, 3, 3, 3);

            var sb = new StringBuilder();
            sb.AppendLine("A = [[1,2,3],[4,5,6],[7,8,9]]");
            sb.AppendLine("B = [[9,8,7],[6,5,4],[3,2,1]]");
            sb.AppendLine("C = A × B (C++ MatMul):");
            for (int i = 0; i < 3; i++)
                sb.AppendLine($"  [{c[i * 3]:F0}, {c[i * 3 + 1]:F0}, {c[i * 3 + 2]:F0}]");
            ResultText = sb.ToString();
            AddLog("矩阵乘法完成（3×3, C++ Native）");
        }

        private void RunFilter()
        {
            var raw = Enumerable.Range(0, 30)
                .Select(i => Math.Sin(i * 0.3) * 50 + _rnd.NextDouble() * 20)
                .ToArray();
            var filtered = NativeHelper.MovingAverage(raw, 5);

            var sb = new StringBuilder();
            sb.AppendLine("移动平均滤波（窗口=5，C++ Native）");
            sb.AppendLine($"原始: [{string.Join(", ", raw.Take(8).Select(v => $"{v:F1}"))} ...]");
            sb.AppendLine($"滤波: [{string.Join(", ", filtered.Take(8).Select(v => $"{v:F1}"))} ...]");
            ResultText = sb.ToString();
            AddLog("移动平均滤波完成");
        }

        private void RunString()
        {
            var input = "Hello SmartMES from C++ Native!";
            var upper = NativeHelper.ToUpperNative(input);
            ResultText = $"原始: {input}\n大写: {upper}";
            AddLog($"字符串处理: {input} -> {upper}");
        }

        private void AddLog(string msg)
        {
            Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
            if (Logs.Count > 100) Logs.RemoveAt(Logs.Count - 1);
        }
    }
}
