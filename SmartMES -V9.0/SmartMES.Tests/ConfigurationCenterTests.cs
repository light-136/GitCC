using SmartMES.Core.Models;
using SmartMES.Services;

namespace SmartMES.Tests;

/// <summary>
/// 配置中心单元测试。
/// 覆盖：初始化默认值、加载/保存、导出/导入、校验、Set链式调用、环境切换。
/// 所有涉及文件的测试使用临时目录，测试完成后自动清理。
/// </summary>
public class ConfigurationCenterTests : IDisposable
{
    // 每个测试使用独立的临时目录，避免测试间干扰
    private readonly string _tempDir;

    public ConfigurationCenterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SmartMES_UT_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>测试结束后清理临时目录</summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ════════ 初始化测试 ════════

    [Fact]
    public void Constructor_默认环境应为prod()
    {
        var center = new ConfigurationCenter(_tempDir);
        Assert.Equal("prod", center.Environment);
    }

    [Fact]
    public void Constructor_Config默认值应存在()
    {
        var center = new ConfigurationCenter(_tempDir);
        // 未加载时使用 new AppConfiguration() 默认值
        Assert.Equal("Simulation", center.Config.RunMode);
        Assert.Equal(9000, center.Config.TcpServerPort);
        Assert.Equal(502, center.Config.ModbusPort);
    }

    [Fact]
    public void Constructor_自定义环境名应正确存储()
    {
        var center = new ConfigurationCenter(_tempDir, "test");
        Assert.Equal("test", center.Environment);
    }

    // ════════ 加载/保存测试 ════════

    [Fact]
    public async Task LoadAsync_文件不存在时应创建默认配置文件()
    {
        var center = new ConfigurationCenter(_tempDir, "dev");
        await center.LoadAsync();

        var expectedFile = Path.Combine(_tempDir, "appsettings.dev.json");
        Assert.True(File.Exists(expectedFile));
    }

    [Fact]
    public async Task LoadAsync_创建默认文件后配置应有效()
    {
        var center = new ConfigurationCenter(_tempDir);
        await center.LoadAsync();

        Assert.Equal("Simulation", center.Config.RunMode);
        Assert.Equal(9000, center.Config.TcpServerPort);
    }

    [Fact]
    public async Task SaveAndLoad_配置修改后保存再加载应持久化()
    {
        var center1 = new ConfigurationCenter(_tempDir);
        await center1.LoadAsync();

        // 修改并保存
        center1.Config.TcpServerIp = "192.168.100.10";
        center1.Config.ModbusPort  = 5020;
        await center1.SaveAsync();

        // 重新加载
        var center2 = new ConfigurationCenter(_tempDir);
        await center2.LoadAsync();

        Assert.Equal("192.168.100.10", center2.Config.TcpServerIp);
        Assert.Equal(5020, center2.Config.ModbusPort);
    }

    [Fact]
    public async Task SaveAsync_应触发ConfigurationSaved事件()
    {
        var center = new ConfigurationCenter(_tempDir);
        await center.LoadAsync();

        bool eventFired = false;
        center.ConfigurationSaved += (_, _) => eventFired = true;

        await center.SaveAsync();

        Assert.True(eventFired);
    }

    [Fact]
    public async Task LoadAsync_应触发ConfigurationLoaded事件()
    {
        var center = new ConfigurationCenter(_tempDir);
        AppConfiguration? loaded = null;
        center.ConfigurationLoaded += (_, cfg) => loaded = cfg;

        await center.LoadAsync();

        // 首次加载（文件不存在时）会创建默认并保存，然后返回
        // 第二次加载才触发 ConfigurationLoaded（从已存在文件）
        await center.LoadAsync();
        Assert.NotNull(loaded);
    }

    // ════════ 导出/导入测试 ════════

    [Fact]
    public async Task ExportAndImport_导出再导入后配置应一致()
    {
        var exportFile = Path.Combine(_tempDir, "export_test.json");

        var center1 = new ConfigurationCenter(_tempDir);
        await center1.LoadAsync();
        center1.Config.SystemName = "出口测试系统";
        center1.Config.MesProtocol = "MQTT";

        await center1.ExportAsync(exportFile);

        // 导入到新实例
        var center2 = new ConfigurationCenter(_tempDir, "import");
        await center2.LoadAsync();
        await center2.ImportAsync(exportFile);

        Assert.Equal("出口测试系统", center2.Config.SystemName);
        Assert.Equal("MQTT", center2.Config.MesProtocol);
    }

    [Fact]
    public async Task ExportAsync_应创建JSON文件()
    {
        var exportFile = Path.Combine(_tempDir, "backup.json");
        var center = new ConfigurationCenter(_tempDir);
        await center.LoadAsync();

        await center.ExportAsync(exportFile);

        Assert.True(File.Exists(exportFile));
        var content = await File.ReadAllTextAsync(exportFile);
        Assert.Contains("RunMode", content);  // 确认是有效JSON
    }

    [Fact]
    public async Task ImportAsync_无效JSON文件应抛出异常()
    {
        var badFile = Path.Combine(_tempDir, "bad.json");
        await File.WriteAllTextAsync(badFile, "{ 这不是合法JSON }", System.Text.Encoding.UTF8);

        var center = new ConfigurationCenter(_tempDir);
        await center.LoadAsync();

        await Assert.ThrowsAsync<System.Text.Json.JsonException>(
            () => center.ImportAsync(badFile));
    }

    [Fact]
    public async Task ImportAsync_导入成功后应更新ChangeLog()
    {
        var exportFile = Path.Combine(_tempDir, "for_import.json");
        var center = new ConfigurationCenter(_tempDir);
        await center.LoadAsync();

        await center.ExportAsync(exportFile);

        int logCountBefore = center.ChangeLog.Count;
        await center.ImportAsync(exportFile);

        Assert.True(center.ChangeLog.Count > logCountBefore);
        Assert.Contains(center.ChangeLog, l => l.Contains("导入成功"));
    }

    // ════════ 配置校验测试 ════════

    [Fact]
    public void Validate_默认配置应通过校验()
    {
        var center = new ConfigurationCenter(_tempDir);
        // 不应抛出异常
        var ex = Record.Exception(() => center.Validate());
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_TCP端口为0应校验失败()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Config.TcpServerPort = 0;

        var ex = Assert.Throws<ArgumentException>(() => center.Validate());
        Assert.Contains("TCP端口", ex.Message);
    }

    [Fact]
    public void Validate_TCP端口超过65535应校验失败()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Config.TcpServerPort = 70000;

        var ex = Assert.Throws<ArgumentException>(() => center.Validate());
        Assert.Contains("TCP端口", ex.Message);
    }

    [Fact]
    public void Validate_Modbus端口非法应校验失败()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Config.ModbusPort = -1;

        var ex = Assert.Throws<ArgumentException>(() => center.Validate());
        Assert.Contains("Modbus端口", ex.Message);
    }

    [Fact]
    public void Validate_采样间隔过小应校验失败()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Config.DataSamplingIntervalMs = 5;  // 最小值 10ms

        var ex = Assert.Throws<ArgumentException>(() => center.Validate());
        Assert.Contains("采样间隔", ex.Message);
    }

    [Fact]
    public void Validate_日志保留天数为0应校验失败()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Config.LogRetentionDays = 0;

        var ex = Assert.Throws<ArgumentException>(() => center.Validate());
        Assert.Contains("日志保留天数", ex.Message);
    }

    [Fact]
    public void Validate_温度阈值超出范围应校验失败()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Config.TemperatureAlarmThreshold = 600;  // 最大值 500

        var ex = Assert.Throws<ArgumentException>(() => center.Validate());
        Assert.Contains("温度阈值", ex.Message);
    }

    [Fact]
    public void Validate_最大日志条数过小应校验失败()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Config.MaxLogEntries = 50;  // 最小值 100

        var ex = Assert.Throws<ArgumentException>(() => center.Validate());
        Assert.Contains("最大日志条数", ex.Message);
    }

    [Fact]
    public void Validate_可传入自定义配置对象校验()
    {
        var center = new ConfigurationCenter(_tempDir);
        var badCfg = new AppConfiguration { TcpServerPort = 0 };

        var ex = Assert.Throws<ArgumentException>(() => center.Validate(badCfg));
        Assert.Contains("TCP端口", ex.Message);
    }

    // ════════ Set 链式调用测试 ════════

    [Fact]
    public void Set_单个配置项修改应生效()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Set<string>(cfg => cfg.TcpServerIp = "10.0.0.1", "修改TCP地址");

        Assert.Equal("10.0.0.1", center.Config.TcpServerIp);
    }

    [Fact]
    public void Set_链式调用应全部生效()
    {
        var center = new ConfigurationCenter(_tempDir);

        center
            .Set<string>(cfg => cfg.TcpServerIp    = "10.0.0.1",  "修改TCP")
            .Set<int>   (cfg => cfg.TcpServerPort   = 8888,         "修改端口")
            .Set<string>(cfg => cfg.RunMode          = "Real",       "切换模式");

        Assert.Equal("10.0.0.1", center.Config.TcpServerIp);
        Assert.Equal(8888,       center.Config.TcpServerPort);
        Assert.Equal("Real",     center.Config.RunMode);
    }

    [Fact]
    public void Set_应向ChangeLog追加记录()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Set<string>(cfg => cfg.RunMode = "Real", "切换真实模式");

        Assert.Contains(center.ChangeLog, l => l.Contains("切换真实模式"));
    }

    // ════════ IsSimulationMode 测试 ════════

    [Fact]
    public void IsSimulationMode_默认运行模式应为仿真()
    {
        var center = new ConfigurationCenter(_tempDir);
        Assert.True(center.IsSimulationMode);
    }

    [Fact]
    public void IsSimulationMode_切换为Real后应返回false()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Config.RunMode = "Real";
        Assert.False(center.IsSimulationMode);
    }

    [Fact]
    public void IsSimulationMode_大小写不敏感()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Config.RunMode = "SIMULATION";
        Assert.True(center.IsSimulationMode);
    }

    // ════════ GetModbusEndpoint 测试 ════════

    [Fact]
    public void GetModbusEndpoint_应返回正确格式()
    {
        var center = new ConfigurationCenter(_tempDir);
        center.Config.ModbusHostIp = "192.168.1.200";
        center.Config.ModbusPort   = 502;

        Assert.Equal("192.168.1.200:502", center.GetModbusEndpoint());
    }

    // ════════ 多环境/可用环境测试 ════════

    [Fact]
    public async Task GetAvailableEnvironments_应返回已创建的环境文件()
    {
        var centerDev  = new ConfigurationCenter(_tempDir, "dev");
        var centerProd = new ConfigurationCenter(_tempDir, "prod");
        var centerTest = new ConfigurationCenter(_tempDir, "test");

        await centerDev.LoadAsync();
        await centerProd.LoadAsync();
        await centerTest.LoadAsync();

        var envs = centerProd.GetAvailableEnvironments().ToList();

        Assert.Contains("dev",  envs);
        Assert.Contains("prod", envs);
        Assert.Contains("test", envs);
    }

    [Fact]
    public void GetAvailableEnvironments_目录不存在时应返回空()
    {
        var center = new ConfigurationCenter(Path.Combine(_tempDir, "不存在的子目录"));
        var envs = center.GetAvailableEnvironments();
        Assert.Empty(envs);
    }

    // ════════ 变更日志测试 ════════

    [Fact]
    public async Task ChangeLog_加载和保存都应记录日志()
    {
        var center = new ConfigurationCenter(_tempDir);
        await center.LoadAsync();
        await center.SaveAsync();

        Assert.True(center.ChangeLog.Count >= 2);
    }

    [Fact]
    public async Task ChangeLog_最多保留100条()
    {
        var center = new ConfigurationCenter(_tempDir);
        await center.LoadAsync();

        // 触发 110 次 Set 操作（每次记一条日志）
        for (int i = 0; i < 110; i++)
            center.Set<int>(cfg => cfg.UiRefreshIntervalMs = i, $"循环设置 {i}");

        Assert.True(center.ChangeLog.Count <= 100);
    }
}
