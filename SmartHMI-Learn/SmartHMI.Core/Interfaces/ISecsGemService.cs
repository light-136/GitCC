using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface ISecsGemService
{
    SecsGemStatus Status { get; }
    Task<bool> EnableAsync();
    Task DisableAsync();
    Task<bool> GoOnlineAsync();
    Task GoOfflineAsync();
    Task SendEventAsync(string eventName, Dictionary<string, object> data);
    Task<bool> SendS1F1HeartbeatAsync();
    IReadOnlyList<SecsGemMessage> GetMessageLog(int count = 100);
    event EventHandler<SecsGemMessage>? MessageReceived;
    event EventHandler<SecsGemMessage>? MessageSent;
}
