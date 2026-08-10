using System;
using System.Windows.Input;

namespace TreeViewRenameDemo.Commands
{
    /// <summary>
    /// 通用命令实现类（RelayCommand）
    /// 功能：实现 ICommand 接口，用于 MVVM 模式中的命令绑定
    /// 设计思路：
    /// 1. 封装 Execute 和 CanExecute 逻辑
    /// 2. 支持带参数和不带参数两种模式
    /// 3. 提供 RaiseCanExecuteChanged 方法手动触发 CanExecute 重新评估
    /// </summary>
    public class RelayCommand : ICommand
    {
        #region 私有字段

        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="execute">执行逻辑委托（必须提供）</param>
        /// <param name="canExecute">是否可执行的判断逻辑委托（可选）</param>
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        #endregion

        #region ICommand 接口实现

        /// <summary>
        /// 命令是否可执行变更事件
        /// 当 CanExecute 的返回值可能发生变化时触发
        /// </summary>
        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// 判断命令是否可以执行
        /// </summary>
        /// <param name="parameter">命令参数</param>
        /// <returns>true 表示可以执行，false 表示不可执行</returns>
        public bool CanExecute(object? parameter)
        {
            // 如果未提供 CanExecute 委托，默认总是可执行
            return _canExecute == null || _canExecute(parameter);
        }

        /// <summary>
        /// 执行命令
        /// </summary>
        /// <param name="parameter">命令参数</param>
        public void Execute(object? parameter)
        {
            _execute(parameter);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 手动触发 CanExecuteChanged 事件
        /// 用于强制 WPF 重新评估命令的可执行状态
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        #endregion
    }
}
