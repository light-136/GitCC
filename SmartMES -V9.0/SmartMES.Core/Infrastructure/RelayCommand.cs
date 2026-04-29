using System.Windows.Input;

namespace SmartMES.Core.Infrastructure
{
    /// <summary>
    /// 通用命令实现，将 UI 命令绑定到 ViewModel 中的委托。
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        /// <summary>
        /// 命令可执行状态变化事件，供 UI 刷新按钮可用性。
        /// </summary>
        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// 创建一个无泛型命令。
        /// </summary>
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// 判断命令当前是否允许执行。
        /// </summary>
        public bool CanExecute(object? parameter)
            => _canExecute == null || _canExecute(parameter);

        /// <summary>
        /// 执行命令委托。
        /// </summary>
        public void Execute(object? parameter)
            => _execute(parameter);

        /// <summary>
        /// 主动通知 UI 重新评估按钮可用状态。
        /// </summary>
        public void RaiseCanExecuteChanged()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 强类型命令实现，减少命令执行时的类型转换代码。
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// 创建一个泛型命令。
        /// </summary>
        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// 判断命令当前是否允许执行。
        /// </summary>
        public bool CanExecute(object? parameter)
            => _canExecute == null || _canExecute(parameter is T t ? t : default);

        /// <summary>
        /// 以强类型参数执行命令。
        /// </summary>
        public void Execute(object? parameter)
            => _execute(parameter is T t ? t : default);

        /// <summary>
        /// 主动通知 UI 刷新命令状态。
        /// </summary>
        public void RaiseCanExecuteChanged()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
