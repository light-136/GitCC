namespace SmartHMI.Core.IO;

public interface IIoDevice
{
    bool ReadInput(int address);
    void WriteOutput(int address, bool value);
    double ReadAnalog(int address);
    void WriteAnalog(int address, double value);
    IReadOnlyList<IoChannel> GetChannels();
    event EventHandler<IoChannel>? ChannelChanged;
}
