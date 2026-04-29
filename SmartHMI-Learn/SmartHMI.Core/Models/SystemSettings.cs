namespace SmartHMI.Core.Models;

public class SystemSettings
{
    public string SystemName { get; set; } = "SmartHMI 工业上位机";
    public int DataSamplingIntervalMs { get; set; } = 1000;
    public double TemperatureAlarmThreshold { get; set; } = 85.0;
    public double PressureAlarmThreshold { get; set; } = 10.0;
    public double SpeedAlarmThreshold { get; set; } = 3000.0;
    public string TcpServerIp { get; set; } = "127.0.0.1";
    public int TcpServerPort { get; set; } = 9000;
    public string SerialPortName { get; set; } = "COM1";
    public int SerialBaudRate { get; set; } = 9600;
    public string LogDirectory { get; set; } = "Logs";
    public int LogRetentionDays { get; set; } = 30;
    public int MaxLogEntries { get; set; } = 1000;
    public int ChartHistoryPoints { get; set; } = 60;
    public string DatabasePath { get; set; } = "smartHMI.db";
}
