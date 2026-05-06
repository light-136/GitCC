namespace SmartMES.Core.Models
{
    public enum UserRole { Admin, Operator, Viewer }

    /// <summary>
    /// 页面权限集合：每个布尔属性对应一个导航页面的访问许可
    /// 由用户角色默认权限与个性化授权共同决定
    /// </summary>
    public class PagePermissions
    {
        public bool Dashboard     { get; set; } = true;
        public bool Device        { get; set; } = true;
        public bool Communication { get; set; } = true;
        public bool Alarm         { get; set; } = true;
        public bool Log           { get; set; } = true;
        public bool Automation    { get; set; } = true;
        public bool MesComm       { get; set; } = true;
        public bool FileProcess   { get; set; } = true;
        public bool Database      { get; set; } = true;
        public bool Motion        { get; set; } = true;
        public bool Native        { get; set; } = true;
        public bool Settings      { get; set; } = false; // 普通用户默认无设置权限
        public bool UserManage    { get; set; } = false; // 仅Admin
        public bool Motion10Axis  { get; set; } = true;
        public bool Vision        { get; set; } = true;
        public bool VisionMotion  { get; set; } = true;
        public bool Industrial    { get; set; } = true;
        public bool SecsGem       { get; set; } = true;
        public bool VisionV2      { get; set; } = true;

        /// <summary>根据角色生成默认权限</summary>
        public static PagePermissions ForRole(UserRole role) => role switch
        {
            UserRole.Admin => new PagePermissions
            {
                Settings = true, UserManage = true
            },
            UserRole.Operator => new PagePermissions
            {
                Settings = false, UserManage = false
            },
            UserRole.Viewer => new PagePermissions
            {
                Dashboard=true, Device=true, Alarm=true, Log=true,
                Communication=false, Automation=false, MesComm=false,
                FileProcess=false, Database=false, Motion=false,
                Native=false, Settings=false, UserManage=false,
                Motion10Axis=false, Vision=true, VisionMotion=false,
                Industrial=false, SecsGem=false, VisionV2=false
            },
            _ => new PagePermissions()
        };
    }

    /// <summary>用户模型：描述登录账户、角色与页面权限</summary>
    public class UserModel
    {
        /// <summary>用户唯一标识</summary>
        public Guid   Id          { get; set; } = Guid.NewGuid();
        /// <summary>登录用户名</summary>
        public string Username    { get; set; } = string.Empty;
        /// <summary>展示名称（UI显示）</summary>
        public string DisplayName { get; set; } = string.Empty;
        /// <summary>登录密码（演示环境明文，生产需哈希）</summary>
        public string Password    { get; set; } = string.Empty;
        /// <summary>用户角色</summary>
        public UserRole Role      { get; set; } = UserRole.Operator;
        /// <summary>最后登录时间</summary>
        public DateTime? LastLoginAt { get; set; }
        /// <summary>账号是否启用</summary>
        public bool IsActive      { get; set; } = true;
        /// <summary>页面权限配置</summary>
        public PagePermissions Permissions { get; set; } = new();
    }
}
