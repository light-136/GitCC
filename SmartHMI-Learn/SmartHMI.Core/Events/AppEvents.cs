using SmartHMI.Core.Models;

namespace SmartHMI.Core.Events;

public class DeviceStatusChangedEvent
{
    public string DeviceId { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public DeviceStatus OldStatus { get; init; }
    public DeviceStatus NewStatus { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

public class NewAlarmEvent
{
    public AlarmRecord Alarm { get; init; } = null!;
}

public class AlarmClearedEvent
{
    public Guid AlarmId { get; init; }
}

public class UserLoginEvent
{
    public UserModel? User { get; init; }
    public bool IsLogin { get; init; }
}

public class CommunicationStatusChangedEvent
{
    public string ChannelId { get; init; } = "";
    public string ChannelName { get; init; } = "";
    public ConnectionStatus Status { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

public class AxisStateChangedEvent
{
    public string AxisId { get; init; } = "";
    public AxisState OldState { get; init; }
    public AxisState NewState { get; init; }
}

public class IoChannelChangedEvent
{
    public string ChannelName { get; init; } = "";
    public int Address { get; init; }
    public object? OldValue { get; init; }
    public object? NewValue { get; init; }
}

public class VisionResultEvent
{
    public VisionResult Result { get; init; } = null!;
}

public class RecipeAppliedEvent
{
    public RecipeModel Recipe { get; init; } = null!;
}

public class WorkorderChangedEvent
{
    public WorkorderModel? Workorder { get; init; }
}

public class AiSuggestionEvent
{
    public AiSuggestion Suggestion { get; init; } = null!;
}

public class CloudSyncEvent
{
    public CloudSyncRecord Record { get; init; } = null!;
}

public class SecsGemStateChangedEvent
{
    public SecsGemState NewState { get; init; }
}

public class EStopEvent
{
    public string Reason { get; init; } = "";
    public bool IsActive { get; init; }
}
