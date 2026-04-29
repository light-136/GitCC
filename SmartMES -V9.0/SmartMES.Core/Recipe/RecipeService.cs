using System.Text.Json;

namespace SmartMES.Core.Recipe
{
    /// <summary>配方参数定义，支持值范围校验。</summary>
    public class RecipeParameter
    {
        /// <summary>参数名（英文标识，唯一）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>参数值（字符串形式，解析时按需转换）</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>参数单位（如 mm/s、℃、Bar）</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>参数中文描述</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>允许最小值</summary>
        public double MinValue { get; set; }

        /// <summary>允许最大值</summary>
        public double MaxValue { get; set; } = 9999;

        /// <summary>
        /// 校验当前值是否在允许范围内。
        /// 只对能解析为 double 的值进行范围校验，非数字类型直接返回 true。
        /// </summary>
        public bool IsValid()
        {
            if (!double.TryParse(Value, out double d)) return true;
            return d >= MinValue && d <= MaxValue;
        }
    }

    /// <summary>
    /// 配方状态枚举：草稿 → 已生效 → 已归档。
    /// 只有"已生效"状态的配方可以被激活用于生产。
    /// </summary>
    public enum RecipeStatus
    {
        Draft,    // 草稿：新建或修改中，未审批
        Active,   // 已生效：通过审批，可用于生产
        Archived  // 已归档：历史版本，只读不可激活
    }

    /// <summary>配方变更记录，追溯每次修改的历史。</summary>
    public class RecipeChangeLog
    {
        /// <summary>变更时间</summary>
        public DateTime ChangedAt { get; set; } = DateTime.Now;

        /// <summary>变更操作人</summary>
        public string ChangedBy { get; set; } = "System";

        /// <summary>变更描述</summary>
        public string ChangeDescription { get; set; } = "";

        /// <summary>变更前版本号</summary>
        public string FromVersion { get; set; } = "";

        /// <summary>变更后版本号</summary>
        public string ToVersion { get; set; } = "";
    }

    /// <summary>
    /// 配方模型（升级版）。
    /// 新增：状态管理、变更日志、版本自增、参数校验。
    /// </summary>
    public class RecipeModel
    {
        /// <summary>配方唯一标识</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>配方名称（唯一）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>产品编码</summary>
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 配方版本号（格式：主版本.次版本，如 1.0 / 2.3）。
        /// 每次通过 BumpVersion() 自动递增次版本号。
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>配方状态（草稿/已生效/已归档）</summary>
        public RecipeStatus Status { get; set; } = RecipeStatus.Draft;

        /// <summary>配方描述</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>创建时间</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>最后更新时间</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>审批人（状态变为 Active 时记录）</summary>
        public string ApprovedBy { get; set; } = "";

        /// <summary>审批时间</summary>
        public DateTime? ApprovedAt { get; set; }

        /// <summary>配方参数集合</summary>
        public List<RecipeParameter> Parameters { get; set; } = new();

        /// <summary>变更日志（按时间倒序，最新在前）</summary>
        public List<RecipeChangeLog> ChangeLogs { get; set; } = new();

        // ────────── 方法 ──────────

        /// <summary>
        /// 按参数名读取参数值，未找到返回 null。
        /// </summary>
        public string? GetParam(string name) =>
            Parameters.FirstOrDefault(p => p.Name == name)?.Value;

        /// <summary>
        /// 按参数名设置参数值，并更新 UpdatedAt 时间戳。
        /// 若参数不存在，直接返回 false。
        /// </summary>
        public bool SetParam(string name, string value)
        {
            var p = Parameters.FirstOrDefault(x => x.Name == name);
            if (p == null) return false;
            p.Value = value;
            UpdatedAt = DateTime.Now;
            return true;
        }

        /// <summary>
        /// 递增次版本号（如 1.0 → 1.1 → 1.2）。
        /// 主版本号保持不变，次版本号自增 1。
        /// </summary>
        public void BumpVersion()
        {
            var parts = Version.Split('.');
            int major = int.TryParse(parts[0], out var m) ? m : 1;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n + 1 : 1;
            Version = $"{major}.{minor}";
            UpdatedAt = DateTime.Now;
        }

        /// <summary>
        /// 校验所有参数的值范围，返回不合法的参数名称列表。
        /// </summary>
        public List<string> Validate()
        {
            return Parameters
                .Where(p => !p.IsValid())
                .Select(p => $"{p.Name}（值:{p.Value}，范围:{p.MinValue}~{p.MaxValue} {p.Unit}）")
                .ToList();
        }

        /// <summary>
        /// 添加一条变更日志记录（最多保留 50 条历史）。
        /// </summary>
        public void AddChangeLog(string description, string changedBy = "System")
        {
            ChangeLogs.Insert(0, new RecipeChangeLog
            {
                ChangedAt         = DateTime.Now,
                ChangedBy         = changedBy,
                ChangeDescription = description,
                FromVersion       = Version,
                ToVersion         = Version
            });

            if (ChangeLogs.Count > 50)
                ChangeLogs.RemoveAt(ChangeLogs.Count - 1);
        }
    }

    /// <summary>配方服务接口（扩展版）。</summary>
    public interface IRecipeService
    {
        IReadOnlyList<RecipeModel> GetAll();
        RecipeModel? GetByName(string name);
        RecipeModel? ActiveRecipe { get; }
        bool Activate(string name);
        void Add(RecipeModel recipe);
        void Remove(string name);
        Task SaveAsync(string filePath);
        Task LoadAsync(string filePath);

        // 版本管理新增接口
        bool Approve(string name, string approvedBy);
        bool Archive(string name);
        RecipeModel? CloneAsNewVersion(string name, string newVersionDescription = "");
        List<string> ValidateRecipe(string name);

        event EventHandler<RecipeModel>? RecipeActivated;
        event EventHandler<RecipeModel>? RecipeStatusChanged;
    }

    /// <summary>
    /// 配方管理服务（升级版 v2）。
    /// 在原有基础上新增：
    ///   1. 状态管理（草稿→审批→归档）
    ///   2. 变更日志追溯
    ///   3. 克隆为新版本
    ///   4. 参数合法性校验
    ///   5. 审批人记录
    /// </summary>
    public class RecipeService : IRecipeService
    {
        private readonly List<RecipeModel> _recipes = new();
        private readonly object _lock = new();

        /// <summary>当前激活的配方（仅 Active 状态可激活）</summary>
        public RecipeModel? ActiveRecipe { get; private set; }

        /// <summary>配方激活事件</summary>
        public event EventHandler<RecipeModel>? RecipeActivated;

        /// <summary>配方状态变更事件</summary>
        public event EventHandler<RecipeModel>? RecipeStatusChanged;

        /// <summary>
        /// 构造函数：初始化三个示例配方并直接设置为 Active 状态，激活默认配方。
        /// </summary>
        public RecipeService()
        {
            var pa = BuildSampleRecipe("产品A-标准", "PA001", 80, 2.5, 120);
            var pb = BuildSampleRecipe("产品B-精密", "PB002", 60, 1.8, 180);
            var pc = BuildSampleRecipe("产品C-高速", "PC003", 120, 3.2, 90);

            // 示例配方直接设为已生效状态（演示用）
            pa.Status = RecipeStatus.Active;
            pb.Status = RecipeStatus.Active;
            pc.Status = RecipeStatus.Active;

            Add(pa); Add(pb); Add(pc);
            Activate("产品A-标准");
        }

        // ════════ 基础 CRUD ════════

        /// <summary>获取所有配方的快照列表（线程安全）</summary>
        public IReadOnlyList<RecipeModel> GetAll()
        {
            lock (_lock) return _recipes.ToList();
        }

        /// <summary>按名称查找配方</summary>
        public RecipeModel? GetByName(string name)
        {
            lock (_lock) return _recipes.FirstOrDefault(r => r.Name == name);
        }

        /// <summary>
        /// 激活指定配方。仅 Active 状态的配方可以被激活用于生产。
        /// </summary>
        public bool Activate(string name)
        {
            lock (_lock)
            {
                var r = _recipes.FirstOrDefault(x => x.Name == name);
                if (r == null || r.Status != RecipeStatus.Active) return false;

                ActiveRecipe = r;
                RecipeActivated?.Invoke(this, r);
                return true;
            }
        }

        /// <summary>添加新配方（若名称重复则拒绝）</summary>
        public void Add(RecipeModel recipe)
        {
            lock (_lock)
            {
                if (_recipes.Any(r => r.Name == recipe.Name))
                    throw new InvalidOperationException($"配方名称已存在：{recipe.Name}");
                _recipes.Add(recipe);
            }
        }

        /// <summary>删除配方（已激活的配方不可删除）</summary>
        public void Remove(string name)
        {
            lock (_lock)
            {
                if (ActiveRecipe?.Name == name)
                    throw new InvalidOperationException("当前激活配方不可删除，请先切换其他配方");
                _recipes.RemoveAll(r => r.Name == name);
            }
        }

        // ════════ 版本管理新增方法 ════════

        /// <summary>
        /// 审批配方（草稿 → 已生效）。
        /// 只有 Draft 状态的配方才可被审批。
        /// </summary>
        /// <param name="name">配方名称</param>
        /// <param name="approvedBy">审批人名称</param>
        public bool Approve(string name, string approvedBy)
        {
            lock (_lock)
            {
                var r = _recipes.FirstOrDefault(x => x.Name == name);
                if (r == null || r.Status != RecipeStatus.Draft) return false;

                var oldVersion = r.Version;
                r.Status     = RecipeStatus.Active;
                r.ApprovedBy = approvedBy;
                r.ApprovedAt = DateTime.Now;
                r.UpdatedAt  = DateTime.Now;
                r.AddChangeLog($"审批通过，由草稿变为已生效", approvedBy);

                RecipeStatusChanged?.Invoke(this, r);
                return true;
            }
        }

        /// <summary>
        /// 归档配方（已生效 → 已归档）。
        /// 归档后配方只读，不可再激活。
        /// 当前激活配方不可归档。
        /// </summary>
        public bool Archive(string name)
        {
            lock (_lock)
            {
                var r = _recipes.FirstOrDefault(x => x.Name == name);
                if (r == null || r.Status != RecipeStatus.Active) return false;
                if (ActiveRecipe?.Name == name) return false;  // 当前激活配方不可归档

                r.Status = RecipeStatus.Archived;
                r.UpdatedAt = DateTime.Now;
                r.AddChangeLog("配方已归档，不可再激活");

                RecipeStatusChanged?.Invoke(this, r);
                return true;
            }
        }

        /// <summary>
        /// 将现有配方克隆为新版本（草稿状态）。
        /// 新版本名称格式：{原名称} V{主版本+1}.0
        /// 保留原配方参数，可在此基础上修改后审批。
        /// </summary>
        /// <param name="name">原配方名称</param>
        /// <param name="newVersionDescription">新版本变更说明</param>
        public RecipeModel? CloneAsNewVersion(string name, string newVersionDescription = "")
        {
            lock (_lock)
            {
                var src = _recipes.FirstOrDefault(x => x.Name == name);
                if (src == null) return null;

                // 提取主版本号并+1
                var parts = src.Version.Split('.');
                int major = int.TryParse(parts[0], out var m) ? m + 1 : 2;
                string newVersion = $"{major}.0";
                string newName    = $"{name.Split(' ')[0]} V{newVersion}";

                // 避免重名
                if (_recipes.Any(r => r.Name == newName))
                    newName = $"{newName}_{DateTime.Now:MMddHHmm}";

                var clone = new RecipeModel
                {
                    Name        = newName,
                    ProductCode = src.ProductCode,
                    Version     = newVersion,
                    Status      = RecipeStatus.Draft,
                    Description = string.IsNullOrEmpty(newVersionDescription)
                        ? $"基于 {src.Name} {src.Version} 克隆的新版本"
                        : newVersionDescription,
                    Parameters  = src.Parameters.Select(p => new RecipeParameter
                    {
                        Name        = p.Name,
                        Value       = p.Value,
                        Unit        = p.Unit,
                        Description = p.Description,
                        MinValue    = p.MinValue,
                        MaxValue    = p.MaxValue
                    }).ToList()
                };

                clone.AddChangeLog($"克隆自 {src.Name} v{src.Version}，变更说明：{newVersionDescription}");
                _recipes.Add(clone);
                return clone;
            }
        }

        /// <summary>
        /// 校验配方所有参数的值范围。
        /// </summary>
        /// <returns>不合法的参数描述列表（空列表表示全部合法）</returns>
        public List<string> ValidateRecipe(string name)
        {
            lock (_lock)
            {
                var r = _recipes.FirstOrDefault(x => x.Name == name);
                return r?.Validate() ?? new List<string> { "配方不存在" };
            }
        }

        // ════════ 持久化 ════════

        /// <summary>将当前配方集合保存为 JSON 文件（UTF-8 编码）</summary>
        public async Task SaveAsync(string filePath)
        {
            List<RecipeModel> copy;
            lock (_lock) copy = _recipes.ToList();

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(copy, options);
            await File.WriteAllTextAsync(filePath, json, System.Text.Encoding.UTF8);
        }

        /// <summary>从 JSON 文件加载配方集合，替换内存中的现有数据</summary>
        public async Task LoadAsync(string filePath)
        {
            if (!File.Exists(filePath)) return;

            var json = await File.ReadAllTextAsync(filePath, System.Text.Encoding.UTF8);
            var list = JsonSerializer.Deserialize<List<RecipeModel>>(json);
            if (list == null) return;

            lock (_lock)
            {
                _recipes.Clear();
                _recipes.AddRange(list);
            }
        }

        // ════════ 私有辅助 ════════

        /// <summary>构建示例配方模型（工厂方法）</summary>
        private static RecipeModel BuildSampleRecipe(
            string name, string code, double speed, double pressure, double temp)
        {
            return new RecipeModel
            {
                Name        = name,
                ProductCode = code,
                Version     = "1.0",
                Description = $"{name} 标准生产参数",
                Parameters  = new List<RecipeParameter>
                {
                    new() { Name="Speed",       Value=speed.ToString(),    Unit="mm/s", Description="运动速度",   MinValue=10,    MaxValue=500  },
                    new() { Name="Pressure",    Value=pressure.ToString(), Unit="Bar",  Description="工作气压",   MinValue=0.5,   MaxValue=10   },
                    new() { Name="Temperature", Value=temp.ToString(),     Unit="℃",   Description="工艺温度",   MinValue=20,    MaxValue=300  },
                    new() { Name="Cycles",      Value="10",                Unit="次",   Description="生产循环数", MinValue=1,     MaxValue=9999 },
                    new() { Name="Tolerance",   Value="0.02",              Unit="mm",   Description="尺寸公差",   MinValue=0.001, MaxValue=1    },
                }
            };
        }
    }
}
