using System.Globalization;
using System.IO;
using System.Text;
using LaserDataFilter.Models;

namespace LaserDataFilter.Services
{
    public class DataFilterService
    {
        private readonly string _dataDirectory;

        public DataFilterService(string dataDirectory)
        {
            _dataDirectory = dataDirectory;
        }

        public List<string> GetAllSeries()
        {
            var series = new HashSet<string>();
            foreach (var file in Directory.GetFiles(_dataDirectory, "*.csv"))
            {
                var parsed = ParseFileName(Path.GetFileNameWithoutExtension(file));
                if (parsed != null)
                    series.Add(parsed.Value.series);
            }
            return series.OrderBy(s => s).ToList();
        }

        public List<DateTime> GetAvailableDates(string series)
        {
            var dates = new HashSet<DateTime>();
            foreach (var file in Directory.GetFiles(_dataDirectory, "*.csv"))
            {
                var parsed = ParseFileName(Path.GetFileNameWithoutExtension(file));
                if (parsed != null && parsed.Value.series == series)
                {
                    if (TryParseDate(parsed.Value.timestamp, out var date))
                        dates.Add(date);
                }
            }
            return dates.OrderBy(d => d).ToList();
        }

        public List<MeasurementFile> FilterFiles(string series, DateTime date)
        {
            var result = new List<MeasurementFile>();
            var dateStr = date.ToString("yyyyMMdd");

            foreach (var file in Directory.GetFiles(_dataDirectory, "*.csv"))
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var parsed = ParseFileName(fileName);
                if (parsed == null) continue;
                if (parsed.Value.series != series) continue;
                if (!parsed.Value.timestamp.StartsWith(dateStr)) continue;

                var mf = new MeasurementFile
                {
                    FilePath = file,
                    Series = parsed.Value.series,
                    Judgment = parsed.Value.judgment,
                    TimeStamp = parsed.Value.timestamp,
                    Index = parsed.Value.index
                };
                result.Add(mf);
            }

            result.Sort((a, b) => a.Index.CompareTo(b.Index));
            return result;
        }

        public List<SummaryRow> BuildSummary(List<MeasurementFile> files)
        {
            var rows = new List<SummaryRow>();
            foreach (var file in files)
            {
                ParseFileContent(file);
                rows.Add(new SummaryRow
                {
                    Time = file.FirstTime.ToString("yyyy-MM-dd HH:mm"),
                    Values = file.Values,
                    Result = file.Result,
                    UniqueId = file.UniqueId
                });
            }
            return rows;
        }

        public string ExportCsv(string series, DateTime date, List<SummaryRow> rows, string? outputDirectory = null)
        {
            if (rows.Count == 0) return string.Empty;

            int maxValues = rows.Max(r => r.Values.Count);
            var sb = new StringBuilder();

            sb.Append("时间");
            for (int i = 1; i <= maxValues; i++)
                sb.Append($",检测值{i}");
            sb.Append(",判定结果");
            sb.AppendLine(",唯一标识号");

            foreach (var row in rows)
            {
                sb.Append("=\"" + row.Time + "\"");
                for (int i = 0; i < maxValues; i++)
                {
                    sb.Append(',');
                    if (i < row.Values.Count)
                        sb.Append(row.Values[i]);
                }
                sb.Append(',');
                sb.Append(row.Result);
                sb.Append(',');
                sb.AppendLine(row.UniqueId);
            }

            var outputFileName = $"{series}_{date:yyyyMMdd}_总表.csv";
            var dir = outputDirectory ?? _dataDirectory;
            var outputPath = Path.Combine(dir, outputFileName);
            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
            return outputPath;
        }

        private void ParseFileContent(MeasurementFile mf)
        {
            var lines = File.ReadAllLines(mf.FilePath, Encoding.Default);
            bool headerSkipped = false;
            string result = string.Empty;
            DateTime firstTime = DateTime.MinValue;
            var values = new List<string>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("检测规格系列:") || line.StartsWith("判定结果:") ||
                    line.StartsWith("检测人员:") || line.StartsWith("唯一标识号:"))
                {
                    if (line.StartsWith("判定结果:"))
                        result = line.Substring("判定结果:".Length).Trim();
                    if (line.StartsWith("唯一标识号:"))
                        mf.UniqueId = line.Substring("唯一标识号:".Length).Trim();
                    continue;
                }

                if (!headerSkipped)
                {
                    headerSkipped = true;
                    continue;
                }

                var parts = SplitCsvLine(line);
                if (parts.Count >= 3)
                {
                    if (firstTime == DateTime.MinValue)
                    {
                        var timeStr = parts[0].TrimStart('=').Trim('"', ' ');
                        DateTime.TryParse(timeStr, out firstTime);
                    }
                    values.Add(parts[2].Trim('"', ' '));
                }
            }

            mf.FirstTime = firstTime;
            mf.Values = values;
            mf.Result = result;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuote = false;
            var current = new StringBuilder();

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuote = !inQuote;
                    current.Append(c);
                }
                else if (c == ',' && !inQuote)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result;
        }

        private static (string series, string judgment, string timestamp, int index)? ParseFileName(string fileName)
        {
            var parts = fileName.Split('_');
            if (parts.Length != 4) return null;
            if (string.IsNullOrWhiteSpace(parts[1])) return null;
            if (!int.TryParse(parts[3], out int index)) return null;
            return (parts[0], parts[1], parts[2], index);
        }

        private static bool TryParseDate(string timestamp, out DateTime date)
        {
            date = DateTime.MinValue;
            if (timestamp.Length < 8) return false;
            return DateTime.TryParseExact(timestamp.Substring(0, 8), "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
    }
}
