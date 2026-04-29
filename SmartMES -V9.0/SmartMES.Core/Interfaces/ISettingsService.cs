using SmartMES.Core.Models;

namespace SmartMES.Core.Interfaces
{
    /// <summary>
    /// 配置服务接口
    /// 负责系统参数的读取、保存，支持JSON持久化
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>当前系统配置</summary>
        SystemSettings Settings { get; }

        /// <summary>
        /// 保存配置到本地文件
        /// </summary>
        Task SaveAsync();

        /// <summary>
        /// 从本地文件加载配置
        /// </summary>
        Task LoadAsync();

        /// <summary>配置变更事件</summary>
        event EventHandler? SettingsChanged;
    }
}
