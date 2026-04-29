using SmartHMI.Core.Models;
using SmartHMI.Modules.Motion;

namespace SmartHMI.UI.ViewModels;

public class MotionViewModel : BaseViewModel
{
    private readonly MotionManager _motionManager;
    private bool _isBusy;
    private string _statusMessage = "就绪";
    private double _targetX, _targetY, _targetZ;
    private double _jogVelocity = 10.0;

    public AxisViewModel AxisX { get; }
    public AxisViewModel AxisY { get; }
    public AxisViewModel AxisZ { get; }

    public bool IsBusy { get => _isBusy; set => SetField(ref _isBusy, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public double TargetX { get => _targetX; set => SetField(ref _targetX, value); }
    public double TargetY { get => _targetY; set => SetField(ref _targetY, value); }
    public double TargetZ { get => _targetZ; set => SetField(ref _targetZ, value); }
    public double JogVelocity { get => _jogVelocity; set => SetField(ref _jogVelocity, value); }

    public RelayCommand EnableAllCommand { get; }
    public RelayCommand HomeAllCommand { get; }
    public RelayCommand EStopCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand MoveToCommand { get; }

    public MotionViewModel(MotionManager motionManager)
    {
        _motionManager = motionManager;

        AxisX = new AxisViewModel(motionManager.Axes["X"]);
        AxisY = new AxisViewModel(motionManager.Axes["Y"]);
        AxisZ = new AxisViewModel(motionManager.Axes["Z"]);

        EnableAllCommand = new RelayCommand(async _ =>
        {
            IsBusy = true;
            await _motionManager.EnableAllAsync();
            StatusMessage = "所有轴已使能";
            IsBusy = false;
        });

        HomeAllCommand = new RelayCommand(async _ =>
        {
            IsBusy = true;
            StatusMessage = "正在回零...";
            await _motionManager.HomeAllAsync();
            StatusMessage = "回零完成";
            IsBusy = false;
        });

        EStopCommand = new RelayCommand(_ =>
        {
            _motionManager.EStopAll();
            StatusMessage = "急停触发！";
        });

        ResetCommand = new RelayCommand(_ =>
        {
            _motionManager.ResetAll();
            StatusMessage = "已复位";
        });

        MoveToCommand = new RelayCommand(_ =>
        {
            _motionManager.Axes["X"].MoveToPosition(TargetX);
            _motionManager.Axes["Y"].MoveToPosition(TargetY);
            _motionManager.Axes["Z"].MoveToPosition(TargetZ);
            StatusMessage = $"移动至 X:{TargetX:F1} Y:{TargetY:F1} Z:{TargetZ:F1}";
        });
    }
}

public class AxisViewModel : BaseViewModel
{
    private readonly AxisController _controller;

    public string Name => _controller.Axis.Name;
    public double Position => _controller.Axis.Position;
    public double Velocity => _controller.Axis.Velocity;
    public string State => _controller.Axis.State.ToString();
    public bool IsEnabled => _controller.Axis.IsEnabled;
    public bool IsHomed => _controller.Axis.IsHomed;
    public string? FaultMessage => _controller.Axis.FaultMessage;

    public RelayCommand JogPlusCommand { get; }
    public RelayCommand JogMinusCommand { get; }
    public RelayCommand StopJogCommand { get; }

    public AxisViewModel(AxisController controller)
    {
        _controller = controller;
        _controller.AxisUpdated += (_, _) => App.Current.Dispatcher.Invoke(() =>
        {
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(Velocity));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(IsHomed));
            OnPropertyChanged(nameof(FaultMessage));
        });

        JogPlusCommand = new RelayCommand(_ => _controller.StartJog(10));
        JogMinusCommand = new RelayCommand(_ => _controller.StartJog(-10));
        StopJogCommand = new RelayCommand(_ => _controller.StopJog());
    }
}
