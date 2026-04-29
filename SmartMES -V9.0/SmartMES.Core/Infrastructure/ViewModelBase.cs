using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartMES.Core.Infrastructure
{
    /// <summary>
    /// ViewModel基类
    /// 实现 INotifyPropertyChanged 接口，为所有ViewModel提供属性变更通知能力
    /// MVVM架构的核心基础设施，所有ViewModel都应继承此类
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        /// <summary>
        /// 属性变更事件
        /// 当属性值发生变化时触发，通知UI层更新绑定的控件
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 触发属性变更通知
        /// 使用 CallerMemberName 特性自动获取调用方属性名，无需手动传入字符串
        /// </summary>
        /// <param name="propertyName">属性名（自动从调用方获取）</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            // 使用?.Invoke安全调用，避免事件为null时的异常
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 设置属性值并触发变更通知（泛型版本）
        /// 标准MVVM属性设置模式：先比较值，变化时赋值并通知
        /// </summary>
        /// <typeparam name="T">属性类型</typeparam>
        /// <param name="field">字段引用（ref参数，允许修改字段值）</param>
        /// <param name="value">新值</param>
        /// <param name="propertyName">属性名（自动从调用方获取）</param>
        /// <returns>值是否发生了变化</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            // 使用EqualityComparer比较值，避免不必要的通知
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
