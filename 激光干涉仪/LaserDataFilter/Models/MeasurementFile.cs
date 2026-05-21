namespace LaserDataFilter.Models
{
    public class MeasurementFile
    {
        public string FilePath { get; set; } = string.Empty;
        public string Series { get; set; } = string.Empty;
        public string Judgment { get; set; } = string.Empty;
        public string TimeStamp { get; set; } = string.Empty;
        public int Index { get; set; }
        public DateTime FirstTime { get; set; }
        public List<string> Values { get; set; } = new();
        public string Result { get; set; } = string.Empty;
        public string UniqueId { get; set; } = string.Empty;
    }

    public class SummaryRow
    {
        public string Time { get; set; } = string.Empty;
        public List<string> Values { get; set; } = new();
        public string Result { get; set; } = string.Empty;
        public string UniqueId { get; set; } = string.Empty;
    }
}
