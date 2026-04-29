using SmartHMI.Core.Interfaces;
using SmartHMI.Modules.Safety;

namespace SmartHMI.UI.ViewModels;

public class SafetyViewModel : BaseViewModel
{
    private readonly ISafetyInterlockService _safety;

    public bool IsAllSafe => _safety.IsAllSafe;
    public bool IsEStopActive => _safety.IsEStopActive;

    private string _eStopReason = "";
    public string EStopReason { get => _eStopReason; set => SetField(ref _eStopReason, value); }

    private string _statusMessage = "系统安全";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public System.Collections.ObjectModel.ObservableCollection<ConditionItem> Conditions { get; } = new();

    public RelayCommand TriggerEStopCommand { get; }
    public RelayCommand ResetEStopCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public SafetyViewModel(ISafetyInterlockService safety)
    {
        _safety = safety;

        TriggerEStopCommand = new RelayCommand(_ => TriggerEStop());
        ResetEStopCommand = new RelayCommand(_ => ResetEStop(), _ => _safety.IsEStopActive);
        RefreshCommand = new RelayCommand(_ => Refresh());

        _safety.EStopTriggered += (_, reason) =>
        {
            EStopReason = reason;
            StatusMessage = $"急停已触发：{reason}";
            Refresh();
        };
        _safety.EStopReset += (_, _) =>
        {
            EStopReason = "";
            StatusMessage = "急停已复位，系统安全";
            Refresh();
        };

        // 注册仿真安全条件
        _safety.RegisterCondition("门禁传感器", () => true, "安全门已关闭");
        _safety.RegisterCondition("光幕传感器", () => true, "光幕无遮挡");
        _safety.RegisterCondition("气压检测", () => true, "气压正常（≥5bar）");
        _safety.RegisterCondition("温度检测", () => true, "设备温度正常（<85°C）");

        Refresh();
    }

    private void Refresh()
    {
        Conditions.Clear();
        foreach (var (name, isSafe, desc) in _safety.GetConditions())
            Conditions.Add(new ConditionItem { Name = name, IsSafe = isSafe, Description = desc });
        OnPropertyChanged(nameof(IsAllSafe));
        OnPropertyChanged(nameof(IsEStopActive));
    }

    private void TriggerEStop() => _safety.TriggerEStop("手动触发急停");
    private void ResetEStop() => _safety.ResetEStop();
}

public class ConditionItem
{
    public string Name { get; set; } = "";
    public bool IsSafe { get; set; }
    public string Description { get; set; } = "";
    public string StatusText => IsSafe ? "正常" : "异常";
}
