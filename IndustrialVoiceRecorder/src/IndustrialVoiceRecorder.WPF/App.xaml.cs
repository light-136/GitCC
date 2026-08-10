using IndustrialVoiceRecorder.Application.Services;
using IndustrialVoiceRecorder.Infrastructure.Audio;
using IndustrialVoiceRecorder.Infrastructure.Export;
using IndustrialVoiceRecorder.Infrastructure.Persistence;
using IndustrialVoiceRecorder.Infrastructure.Services;
using IndustrialVoiceRecorder.Infrastructure.SpeechRecognition;
using IndustrialVoiceRecorder.Shared;
using IndustrialVoiceRecorder.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;
using System.Windows;

using WpfApplication = System.Windows.Application;

namespace IndustrialVoiceRecorder.WPF;

/// <summary>
/// 应用程序入口 (v2.1 配置化版)
/// 从 config.txt 读取所有配置
/// </summary>
public partial class App : WpfApplication
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 第一步：加载配置文件
        var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
        AppConfig.Load(configPath);

        // 第二步：初始化目录
        InitializeDirectories();

        // 第三步：配置日志
        ConfigureLogging();

        // 第四步：配置依赖注入
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        ServiceProvider = serviceCollection.BuildServiceProvider();

        Log.Information("应用程序启动 - {AppName} v{Version}", AppConfig.AppName, AppConfig.AppVersion);
        Log.Information("配置文件: {Path}", configPath);
        Log.Information("FunASR地址: {Url}", AppConfig.FunAsrUrl);
    }

    private void InitializeDirectories()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        Directory.CreateDirectory(Path.Combine(appDir, AppConfig.LogDirectory));
        Directory.CreateDirectory(Path.Combine(appDir, AppConfig.ModelDirectory));
        Directory.CreateDirectory(Path.Combine(appDir, AppConfig.ExportDirectory));
        Directory.CreateDirectory(Path.Combine(appDir, AppConfig.DatabaseDirectory));
    }

    private void ConfigureLogging()
    {
        var logPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            AppConfig.LogDirectory,
            "log-.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog();
        });

        // 数据库（路径从配置读取）
        var dbPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            AppConfig.DatabaseDirectory,
            AppConfig.DatabaseFileName);
        var connectionString = $"Data Source={dbPath}";

        services.AddSingleton<IVoiceRecordRepository>(sp =>
            new VoiceRecordRepository(connectionString, sp.GetRequiredService<ILogger<VoiceRecordRepository>>()));

        services.AddSingleton<IAudioCaptureService, NAudioCaptureService>();
        services.AddSingleton<ISpeechRecognitionService, FunAsrRecognitionService>();
        services.AddSingleton<IRecordService, RecordService>();
        services.AddSingleton<IExportService, ExcelExportService>();
        services.AddSingleton<TextProcessor>();
        services.AddSingleton<VoiceProcessingService>();
        services.AddTransient<MainViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("应用程序退出");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
