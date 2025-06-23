using System.Reflection;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils.DB;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.PacketHandlers;
using AAEmu.Login.Models;
using AAEmu.Login.Models.Database;
using AAEmu.Login.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using OSVersionExtension;

namespace AAEmu.Login;

public static class Program
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static readonly Thread _thread = Thread.CurrentThread;
    private static DateTime _startTime;
    private static string Name => Assembly.GetExecutingAssembly().GetName().Name ?? "AAEmu.Login";
    private static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "???";

    public static int UpTime => (int)(DateTime.UtcNow - _startTime).TotalSeconds;

    public static async Task Main(string[] args)
    {
        Initialization();

        var builder = Host.CreateApplicationBuilder(args);

        SetupLogging(builder.Logging);

        builder.Configuration
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Config.json"), optional: true, reloadOnChange: true)
            .AddUserSecrets<LoginService>()
            .AddEnvironmentVariables()
            .AddCommandLine(args);

        // Configure services
        builder.Services.AddOptions();
        builder.Services.AddOptionsWithValidateOnStart<AppConfiguration>()
            .BindConfiguration("")
            .ValidateDataAnnotations();

        builder.Services.AddDbContextFactory<LoginDbContext>((sp, options) =>
        {
            var appConfig = sp.GetRequiredService<IOptions<AppConfiguration>>().Value;
            var connectionString = MySQL.GetConnectionString(appConfig.Connections.MySQLProvider);
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        });

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddHostedService<MySqlInitializer>();
        builder.Services.AddHostedService<LoginService>();

        builder.Services.AddSingleton<IGameController, GameController>();
        builder.Services.AddSingleton<ILoginController, LoginController>();
        builder.Services.AddSingleton<IRequestController, RequestController>();

        builder.Services.AddSingleton<IInternalProtocolHandler, InternalProtocolHandler>();
        builder.Services.AddSingleton<IInternalConnectionTable, InternalConnectionTable>();
        builder.Services.AddSingleton<IInternalNetwork, InternalNetwork>();
        builder.Services.AddSingleton<ILoginProtocolHandler, LoginProtocolHandler>();
        builder.Services.AddSingleton<ILoginConnectionTable, LoginConnectionTable>();
        builder.Services.AddSingleton<ILoginNetwork, LoginNetwork>();

        builder.Services.AddInternalPacketHandlers();
        builder.Services.AddLoginPacketHandlers();

        var app = builder.Build();
        await app.RunAsync();
    }

    private static void SetupLogging(ILoggingBuilder builder)
    {
        LogManager.ThrowConfigExceptions = false;
        LogManager.Configuration = new XmlLoggingConfiguration(Path.Combine(AppContext.BaseDirectory, "NLog.config"));

        builder
            .ClearProviders()
            .AddNLog();
    }

    /// <summary>
    /// Tries to return a more human-readable OS name
    /// </summary>
    /// <returns></returns>
    private static string GetOsName()
    {
        try
        {
            // Note: This NuGet package can throw a exception in some cases, so we try to catch it
            return OSVersion.GetOperatingSystem().ToString();
        }
        catch
        {
            return "Unknown";
        }
    }

    private static void Initialization()
    {
        _thread.Name = "AA.LoginServer Base Thread";
        _startTime = DateTime.UtcNow;
        Logger.Info($"{Name} version {Version}");
        Logger.Info(
            $"Running as {(Environment.Is64BitProcess ? "64" : "32")}-bits on {(Environment.Is64BitOperatingSystem ? "64" : "32")}-bits {GetOsName()} ({Environment.OSVersion})");
        if (!Environment.Is64BitProcess)
        {
            Logger.Warn($"Running in 32-bits mode is not recommended to do memory constraints");
        }
    }
}
