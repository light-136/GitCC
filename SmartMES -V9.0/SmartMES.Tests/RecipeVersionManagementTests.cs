using SmartMES.Core.Recipe;

namespace SmartMES.Tests;

/// <summary>
/// 配方版本管理单元测试。
/// 覆盖：状态流转（草稿→生效→归档）、审批、克隆、参数校验、变更日志。
/// </summary>
public class RecipeVersionManagementTests
{
    // ──────── 辅助工厂方法 ────────

    /// <summary>创建一个草稿状态的测试配方，带有简单参数</summary>
    private static RecipeModel MakeDraftRecipe(string name = "测试配方-UT", string code = "UT001")
    {
        return new RecipeModel
        {
            Name        = name,
            ProductCode = code,
            Version     = "1.0",
            Status      = RecipeStatus.Draft,
            Parameters  = new List<RecipeParameter>
            {
                new() { Name = "Speed",       Value = "100",  Unit = "mm/s", MinValue = 10,  MaxValue = 500  },
                new() { Name = "Temperature", Value = "200",  Unit = "℃",   MinValue = 20,  MaxValue = 300  },
                new() { Name = "Pressure",    Value = "2.5",  Unit = "Bar", MinValue = 0.5, MaxValue = 10   },
            }
        };
    }

    // ════════ 配方状态枚举测试 ════════

    [Fact]
    public void RecipeStatus_默认状态应为Draft()
    {
        var recipe = new RecipeModel();
        Assert.Equal(RecipeStatus.Draft, recipe.Status);
    }

    [Fact]
    public void RecipeStatus_三个枚举值应全部存在()
    {
        var values = Enum.GetValues<RecipeStatus>();
        Assert.Contains(RecipeStatus.Draft,    values);
        Assert.Contains(RecipeStatus.Active,   values);
        Assert.Contains(RecipeStatus.Archived, values);
    }

    // ════════ 审批流程测试（Draft → Active） ════════

    [Fact]
    public void Approve_草稿配方审批成功()
    {
        var svc = new RecipeService();
        svc.Add(MakeDraftRecipe());

        var ok = svc.Approve("测试配方-UT", "张三");

        Assert.True(ok);
        var recipe = svc.GetByName("测试配方-UT")!;
        Assert.Equal(RecipeStatus.Active, recipe.Status);
        Assert.Equal("张三", recipe.ApprovedBy);
        Assert.NotNull(recipe.ApprovedAt);
    }

    [Fact]
    public void Approve_不存在的配方应返回false()
    {
        var svc = new RecipeService();
        var ok = svc.Approve("不存在的配方", "张三");
        Assert.False(ok);
    }

    [Fact]
    public void Approve_已生效配方不可重复审批()
    {
        // 示例配方构造时已经设为 Active
        var svc = new RecipeService();
        var ok = svc.Approve("产品A-标准", "张三");
        Assert.False(ok);  // Active 状态不允许审批
    }

    [Fact]
    public void Approve_已归档配方不可审批()
    {
        var svc = new RecipeService();
        svc.Add(MakeDraftRecipe("归档测试"));
        svc.Approve("归档测试", "审批员");

        // 先激活一个其他配方以便归档这个
        svc.Activate("产品A-标准");
        svc.Archive("归档测试");

        var ok = svc.Approve("归档测试", "二次审批");
        Assert.False(ok);
    }

    [Fact]
    public void Approve_审批成功应触发StatusChanged事件()
    {
        var svc = new RecipeService();
        svc.Add(MakeDraftRecipe());

        RecipeModel? changedRecipe = null;
        svc.RecipeStatusChanged += (_, r) => changedRecipe = r;

        svc.Approve("测试配方-UT", "审批员");

        Assert.NotNull(changedRecipe);
        Assert.Equal("测试配方-UT", changedRecipe!.Name);
    }

    [Fact]
    public void Approve_审批后变更日志应有记录()
    {
        var svc = new RecipeService();
        svc.Add(MakeDraftRecipe());
        svc.Approve("测试配方-UT", "测试员");

        var recipe = svc.GetByName("测试配方-UT")!;
        Assert.NotEmpty(recipe.ChangeLogs);
        Assert.Contains(recipe.ChangeLogs, l => l.ChangedBy == "测试员");
    }

    // ════════ 激活流程测试 ════════

    [Fact]
    public void Activate_草稿配方不可激活()
    {
        var svc = new RecipeService();
        svc.Add(MakeDraftRecipe());

        var ok = svc.Activate("测试配方-UT");  // Draft 状态

        Assert.False(ok);
        Assert.NotEqual("测试配方-UT", svc.ActiveRecipe?.Name);
    }

    [Fact]
    public void Activate_已生效配方可以激活()
    {
        var svc = new RecipeService();
        var ok = svc.Activate("产品B-精密");  // 构造时已设为 Active
        Assert.True(ok);
        Assert.Equal("产品B-精密", svc.ActiveRecipe?.Name);
    }

    [Fact]
    public void Activate_归档配方不可激活()
    {
        var svc = new RecipeService();
        // 先激活产品B，再归档产品C
        svc.Activate("产品B-精密");
        svc.Archive("产品C-高速");

        var ok = svc.Activate("产品C-高速");
        Assert.False(ok);
    }

    // ════════ 归档流程测试（Active → Archived） ════════

    [Fact]
    public void Archive_非激活的已生效配方可归档()
    {
        var svc = new RecipeService();
        // 确保激活的是产品A，归档产品B
        svc.Activate("产品A-标准");
        var ok = svc.Archive("产品B-精密");

        Assert.True(ok);
        Assert.Equal(RecipeStatus.Archived, svc.GetByName("产品B-精密")!.Status);
    }

    [Fact]
    public void Archive_当前激活配方不可归档()
    {
        var svc = new RecipeService();
        // 产品A-标准 是构造时默认激活的
        var ok = svc.Archive("产品A-标准");
        Assert.False(ok);
    }

    [Fact]
    public void Archive_草稿配方不可直接归档()
    {
        var svc = new RecipeService();
        svc.Add(MakeDraftRecipe());

        var ok = svc.Archive("测试配方-UT");  // Draft 状态，Archive 要求 Active
        Assert.False(ok);
    }

    [Fact]
    public void Archive_成功后应触发StatusChanged事件()
    {
        var svc = new RecipeService();
        svc.Activate("产品A-标准");

        RecipeModel? changed = null;
        svc.RecipeStatusChanged += (_, r) => changed = r;

        svc.Archive("产品B-精密");

        Assert.NotNull(changed);
        Assert.Equal("产品B-精密", changed!.Name);
        Assert.Equal(RecipeStatus.Archived, changed.Status);
    }

    // ════════ 克隆新版本测试 ════════

    [Fact]
    public void CloneAsNewVersion_克隆后应创建草稿状态新配方()
    {
        var svc = new RecipeService();
        var clone = svc.CloneAsNewVersion("产品A-标准", "优化速度参数");

        Assert.NotNull(clone);
        Assert.Equal(RecipeStatus.Draft, clone!.Status);
    }

    [Fact]
    public void CloneAsNewVersion_新版本主版本号应递增()
    {
        var svc = new RecipeService();
        var clone = svc.CloneAsNewVersion("产品A-标准");

        // 原版本 1.0，克隆后应为 2.0
        Assert.NotNull(clone);
        Assert.Equal("2.0", clone!.Version);
    }

    [Fact]
    public void CloneAsNewVersion_应继承原配方的所有参数()
    {
        var svc = new RecipeService();
        var original = svc.GetByName("产品A-标准")!;
        var clone = svc.CloneAsNewVersion("产品A-标准");

        Assert.NotNull(clone);
        Assert.Equal(original.Parameters.Count, clone!.Parameters.Count);
        Assert.Equal(original.ProductCode, clone.ProductCode);

        // 参数值应相同
        foreach (var origParam in original.Parameters)
        {
            var cloneParam = clone.Parameters.FirstOrDefault(p => p.Name == origParam.Name);
            Assert.NotNull(cloneParam);
            Assert.Equal(origParam.Value, cloneParam!.Value);
        }
    }

    [Fact]
    public void CloneAsNewVersion_克隆配方自动加入配方列表()
    {
        var svc = new RecipeService();
        int countBefore = svc.GetAll().Count;

        svc.CloneAsNewVersion("产品B-精密", "第二代精密版");

        Assert.Equal(countBefore + 1, svc.GetAll().Count);
    }

    [Fact]
    public void CloneAsNewVersion_不存在配方应返回null()
    {
        var svc = new RecipeService();
        var clone = svc.CloneAsNewVersion("不存在的配方");
        Assert.Null(clone);
    }

    [Fact]
    public void CloneAsNewVersion_克隆配方应有变更日志()
    {
        var svc = new RecipeService();
        var clone = svc.CloneAsNewVersion("产品A-标准", "测试克隆说明");

        Assert.NotNull(clone);
        Assert.NotEmpty(clone!.ChangeLogs);
        // 日志描述应包含克隆来源信息
        Assert.Contains(clone.ChangeLogs, l => l.ChangeDescription.Contains("克隆自"));
    }

    // ════════ 参数校验测试 ════════

    [Fact]
    public void ValidateRecipe_合法参数应返回空列表()
    {
        var svc = new RecipeService();
        var errors = svc.ValidateRecipe("产品A-标准");
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateRecipe_超范围参数应返回错误描述()
    {
        var svc = new RecipeService();
        svc.Add(MakeDraftRecipe("参数校验测试"));

        // 将 Speed 改为超出最大值（MaxValue=500）的值
        var recipe = svc.GetByName("参数校验测试")!;
        recipe.SetParam("Speed", "9999");

        var errors = svc.ValidateRecipe("参数校验测试");

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("Speed"));
    }

    [Fact]
    public void ValidateRecipe_低于最小值应校验失败()
    {
        var svc = new RecipeService();
        svc.Add(MakeDraftRecipe("最小值校验测试"));

        var recipe = svc.GetByName("最小值校验测试")!;
        recipe.SetParam("Temperature", "5");  // MinValue=20

        var errors = svc.ValidateRecipe("最小值校验测试");
        Assert.Contains(errors, e => e.Contains("Temperature"));
    }

    [Fact]
    public void ValidateRecipe_不存在配方应返回错误提示()
    {
        var svc = new RecipeService();
        var errors = svc.ValidateRecipe("不存在的配方");
        Assert.Contains("配方不存在", errors[0]);
    }

    [Fact]
    public void ValidateRecipe_非数字参数不校验范围()
    {
        var recipe = new RecipeModel
        {
            Name = "文本参数测试",
            Parameters = new List<RecipeParameter>
            {
                new() { Name = "Mode", Value = "AUTO", MinValue = 0, MaxValue = 100 }
            }
        };

        // IsValid 对非数字值返回 true
        Assert.True(recipe.Parameters[0].IsValid());
    }

    // ════════ 变更日志测试 ════════

    [Fact]
    public void AddChangeLog_变更日志最多保留50条()
    {
        var recipe = new RecipeModel { Name = "日志上限测试" };

        // 写入 60 条日志
        for (int i = 0; i < 60; i++)
            recipe.AddChangeLog($"变更 {i}");

        Assert.Equal(50, recipe.ChangeLogs.Count);
    }

    [Fact]
    public void AddChangeLog_最新日志在列表头部()
    {
        var recipe = new RecipeModel { Name = "日志顺序测试" };
        recipe.AddChangeLog("第一条");
        recipe.AddChangeLog("第二条");

        // 最新的（第二条）插入到头部
        Assert.Equal("第二条", recipe.ChangeLogs[0].ChangeDescription);
    }

    // ════════ 版本号递增测试 ════════

    [Fact]
    public void BumpVersion_次版本号应递增()
    {
        var recipe = new RecipeModel { Version = "1.0" };
        recipe.BumpVersion();
        Assert.Equal("1.1", recipe.Version);

        recipe.BumpVersion();
        Assert.Equal("1.2", recipe.Version);
    }

    [Fact]
    public void BumpVersion_主版本号保持不变()
    {
        var recipe = new RecipeModel { Version = "3.5" };
        recipe.BumpVersion();
        Assert.Equal("3.6", recipe.Version);
    }

    [Fact]
    public void BumpVersion_应更新UpdatedAt时间戳()
    {
        var recipe = new RecipeModel { Version = "1.0" };
        var before = DateTime.Now.AddSeconds(-1);

        recipe.BumpVersion();

        Assert.True(recipe.UpdatedAt >= before);
    }

    // ════════ 防重名和防删活跃配方测试 ════════

    [Fact]
    public void Add_重复名称应抛出异常()
    {
        var svc = new RecipeService();
        var duplicate = MakeDraftRecipe("产品A-标准");  // 已存在的名称

        Assert.Throws<InvalidOperationException>(() => svc.Add(duplicate));
    }

    [Fact]
    public void Remove_激活配方不可删除()
    {
        var svc = new RecipeService();
        // 产品A-标准 是默认激活的

        Assert.Throws<InvalidOperationException>(() => svc.Remove("产品A-标准"));
    }

    [Fact]
    public void Remove_非激活配方可以删除()
    {
        var svc = new RecipeService();
        svc.Add(MakeDraftRecipe("临时配方"));

        svc.Remove("临时配方");

        Assert.Null(svc.GetByName("临时配方"));
    }
}
