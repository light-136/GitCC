using SmartMES.Core.Infrastructure;
using SmartMES.Core.Interfaces;
using SmartMES.Services.Automation;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartMES.UI.ViewModels
{
    public class AutomationViewModel : ViewModelBase
    {
        private readonly AutomationEngine _engine;
        private readonly ILoggingService _logger;

        public ObservableCollection<StepItemViewModel> Steps { get; } = new();

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (!SetProperty(ref _isRunning, value)) return;
                StartFlowCommand.RaiseCanExecuteChanged();
                ResetFlowCommand.RaiseCanExecuteChanged();
            }
        }

        private string _flowStatus = "就绪";
        public string FlowStatus { get => _flowStatus; set => SetProperty(ref _flowStatus, value); }

        public RelayCommand StartFlowCommand { get; }
        public RelayCommand ResetFlowCommand { get; }

        public AutomationViewModel(AutomationEngine engine, ILoggingService logger)
        {
            _engine = engine;
            _logger = logger;

            foreach (var step in _engine.Steps)
            {
                var vm = new StepItemViewModel(step);
                Steps.Add(vm);
                step.StatusChanged += (_, e) => Application.Current?.Dispatcher.Invoke(() => vm.Refresh(e));
            }

            _engine.FlowCompleted += (_, success) => Application.Current?.Dispatcher.Invoke(() =>
            {
                IsRunning = false;
                FlowStatus = success ? "流程完成 ✔" : "流程失败 ✘";
            });

            _engine.StepChanged += (_, stepName) => Application.Current?.Dispatcher.Invoke(() =>
                FlowStatus = $"执行中：{stepName}");

            StartFlowCommand = new RelayCommand(async _ =>
            {
                IsRunning = true;
                FlowStatus = "流程启动中...";
                _engine.Reset();
                foreach (var s in Steps) s.ResetDisplay();
                await _engine.RunAsync();
            }, _ => !_isRunning);

            ResetFlowCommand = new RelayCommand(_ =>
            {
                _engine.Reset();
                foreach (var s in Steps) s.ResetDisplay();
                FlowStatus = "已重置";
                IsRunning = false;
            }, _ => !_isRunning);
        }
    }

    public class StepItemViewModel : ViewModelBase
    {
        public string Name { get; }
        public string Description { get; }

        private StepStatus _status = StepStatus.Pending;
        public StepStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private string _message = "等待执行";
        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public string StatusIcon => Status switch
        {
            StepStatus.Pending => "○",
            StepStatus.Running => "▶",
            StepStatus.Completed => "✔",
            StepStatus.Failed => "✘",
            _ => "-"
        };

        public StepItemViewModel(AutomationStepBase step)
        {
            Name = step.StepName;
            Description = step.Description;
        }

        public void Refresh(StepStatusChangedArgs e)
        {
            Status = e.Status;
            Message = e.Message;
            OnPropertyChanged(nameof(StatusIcon));
        }

        public void ResetDisplay()
        {
            Status = StepStatus.Pending;
            Message = "等待执行";
            OnPropertyChanged(nameof(StatusIcon));
        }
    }
}
