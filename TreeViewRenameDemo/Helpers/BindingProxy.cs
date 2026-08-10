using System.Windows;

namespace TreeViewRenameDemo.Helpers
{
    /// <summary>
    /// 绑定代理类（BindingProxy）
    /// 功能：解决 ContextMenu 不在可视树中，无法通过 RelativeSource 绑定到 ViewModel 的问题
    /// 设计思路：
    /// 1. 继承 Freezable，使其能作为资源定义在 XAML 中
    /// 2. 定义 Data 依赖属性，存储需要传递的 DataContext
    /// 3. ContextMenu 中通过 StaticResource 引用该代理来访问 ViewModel
    /// </summary>
    public class BindingProxy : Freezable
    {
        #region Freezable 重写

        /// <summary>
        /// 创建 Freezable 实例（Freezable 模式必需重写）
        /// </summary>
        protected override Freezable CreateInstanceCore()
        {
            return new BindingProxy();
        }

        #endregion

        #region 依赖属性

        /// <summary>
        /// Data 依赖属性定义
        /// 用于存储需要传递到 ContextMenu 中的 DataContext（通常是 ViewModel）
        /// </summary>
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(
                nameof(Data),
                typeof(object),
                typeof(BindingProxy),
                new UIPropertyMetadata(null));

        /// <summary>
        /// 数据对象（绑定到 Window 的 DataContext，即 MainViewModel）
        /// </summary>
        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        #endregion
    }
}
