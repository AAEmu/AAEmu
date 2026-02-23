using System.Reflection;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models;
using AAEmu.Game.Services;
using AAEmu.Game.Services.WebApi;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Utils.Scripts;

using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NLog;
using NLog.Config;
using OSVersionExtension;

namespace AAEmu.Game;

public static class Program
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static readonly Thread _thread = Thread.CurrentThread;
    private static DateTime _startTime;
    private static string Name => Assembly.GetExecutingAssembly().GetName().Name;
    private static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "???";
    public static AutoResetEvent ShutdownSignal => new(false); // TODO save to shutdown server?

    public static int UpTime => (int)(DateTime.UtcNow - _startTime).TotalSeconds;
    private static string[] _launchArgs;

    public static async Task<int> Main(string[] args)
    {
        _launchArgs = args;
        Initialization();

        if (args.Length > 0 && args[0] == "compiler-check")
        {
            Logger.Info("Check compilation");
            var result = ScriptCompiler.CompileScriptsWithAllDependencies(out _, out var diagnostics);

            if (result)
            {
                Logger.Info("Compilation successful");
                return 0;
            }
            else
            {
                Logger.Error(new CompilationErrorException("Compilation failed", diagnostics), "Compilation failed");
                return 1;
            }
        }

        Configuration(args);

        // Apply MySQL Configuration
        MySQL.SetConfiguration(AppConfiguration.Instance.Connections.MySQLProvider);

        try
        {
            // Test the DB connection
            var connection = MySQL.CreateConnection();
            connection.Close();
            connection.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex, "MySQL connection failed, check your configuration!");
            LogManager.Flush();
            return 1;
        }

        try
        {
            // Test the DB connection
            using var connection = SQLite.CreateConnection();
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex, "Failed to load compact.sqlite3 database check if it exists!");
            LogManager.Flush();
            return 1;
        }

        // Apply any pending SQLite patches from SQL/patches/compact/
        if (!SQLiteDatabaseUpdater.Run())
        {
            Logger.Fatal("SQLite migration failed! Check the patch files in SQL/patches/compact/");
            LogManager.Flush();
            return 1;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        var builder = new HostBuilder()
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddEnvironmentVariables();

                if (args != null)
                {
                    config.AddCommandLine(args);
                }
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddOptions();
                services.AddSingleton<IHostedService, GameService>();
                services.AddSingleton<IHostedService, WebApiService>();
                services.AddSingleton<IHostedService, DiscordBotService>();
            });

        try
        {
            await builder.RunConsoleAsync();
        }
        catch (OperationCanceledException ocex)
        {
            Logger.Fatal(ocex.Message);
        }
        return 0;
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
        _thread.Name = "AA.Game Base Thread";
        _startTime = DateTime.UtcNow;
        Logger.Info($"{Name} version {Version}");
        Logger.Info($"Running as {(Environment.Is64BitProcess ? "64" : "32")}-bits on {(Environment.Is64BitOperatingSystem ? "64" : "32")}-bits {GetOsName()} ({Environment.OSVersion})");
        if (!Environment.Is64BitProcess)
        {
            Logger.Warn($"Running in 32-bits mode is not recommended to do memory constraints");
        }
    }

    public static void ReloadConfiguration() => Configuration(_launchArgs);

    private static void Configuration(string[] args)
    {
        // Load NLog configuration
        LogManager.ThrowConfigExceptions = false;
        LogManager.Configuration = new XmlLoggingConfiguration(Path.Combine(FileManager.AppPath, "NLog.config"));

        // Load Game server configuration
        List<string> configFiles =
        [
            // Add the old main Config.json file
            Path.Combine(FileManager.AppPath, "Config.json"),
            Path.Combine(FileManager.AppPath, "Config.Local.json"),
            // Get files inside the Configurations folder
            ..Directory.GetFiles(Path.Combine(FileManager.AppPath, "Configurations"), "*.json",
                SearchOption.AllDirectories).Order()
        ];

        var configurationBuilder = new ConfigurationBuilder();

        // Add config json files
        foreach (var file in configFiles)
        {
            Logger.Info($"Config: {file}");
            configurationBuilder.AddJsonFile(file, optional: true);
        }

        configurationBuilder.AddUserSecrets<GameService>();
        configurationBuilder.AddEnvironmentVariables();
        configurationBuilder.AddCommandLine(args);

        var configurationBuilderResult = configurationBuilder.Build();
        configurationBuilderResult.Bind(AppConfiguration.Instance);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exceptionStr = e.ExceptionObject.ToString();
        Logger.Fatal(exceptionStr);
    }
}
