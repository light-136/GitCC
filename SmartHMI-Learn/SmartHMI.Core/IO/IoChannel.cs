namespace SmartHMI.Core.IO;

public enum IoChannelType { DigitalInput, DigitalOutput, AnalogInput, AnalogOutput }

public class IoChannel
{
    public int Address { get; init; }
    public string Name { get; init; } = "";
    public IoChannelType Type { get; init; }
    public object Value { get; set; } = false;
    public DateTime LastUpdated { get; set; } = DateTime.Now;

    public bool AsBool() => Value is bool b && b;
    public double AsDouble() => Value is double d ? d : 0.0;
}
