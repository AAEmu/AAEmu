using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Genesis;
using AAEmu.Game.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Config;

namespace AAEmu.Game
{
    public static class Program
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private static Thread _thread = Thread.CurrentThread;
        private static DateTime _startTime;
        private static string Name => Assembly.GetExecutingAssembly().GetName().Name;
        private static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "???";
        public static AutoResetEvent ShutdownSignal = new AutoResetEvent(false); // TODO save to shutdown server?

        public static int UpTime => (int)(DateTime.UtcNow - _startTime).TotalSeconds;
        private static string[] _launchArgs;

        public static async Task Main(string[] args)
        {
            Initialization();
            _launchArgs = args;
            if (!LoadConfiguration())
            {
                return;
            }

            _log.Info($"{Name} version {Version}");
            
            // Apply MySQL Configuration
            try
            {
                MySQL.SetConfiguration(AppConfiguration.Instance.Connections.MySQLProvider);
            }
            catch
            {
                _log.Fatal("MySQL configuration could not be loaded !");
                return;
            }
            
            // Test the DB connection
            var connection = MySQL.CreateConnection();
            if (connection == null)
            {
                LogManager.Flush();
                return;
            }

            connection.Close();

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
                    services.AddSingleton<IHostedService, DiscordBotService>();
                });

            try
            {
                await builder.RunConsoleAsync();
            }
            catch (OperationCanceledException ocex)
            {
                _log.Fatal(ocex.Message);
            }
        }

        private static void Initialization()
        {
            _thread.Name = "AA.Game Base Thread";
            _startTime = DateTime.UtcNow;
        }

        public static bool LoadConfiguration()
        {
            var mainConfig = Path.Combine(FileManager.AppPath, "Config.json");
            if (!File.Exists(mainConfig))
            {
                _log.Fatal($"{mainConfig} doesn't exist!");
                return false;
            }

            Configuration(_launchArgs, mainConfig);
            return true;            
        }

        private static void Configuration(string[] args, string mainConfigJson)
        {
            // Load NLog configuration
            LogManager.ThrowConfigExceptions = false;
            LogManager.Configuration = new XmlLoggingConfiguration(Path.Combine(FileManager.AppPath, "NLog.config"));

            // Load Game server configuration
            // Get files inside in the Configurations folder
            var configFiles = Directory.GetFiles(Path.Combine(FileManager.AppPath, "Configurations"), "*.json", SearchOption.AllDirectories).ToList();
            configFiles.Sort();
            // Add the old main Config.json file
            configFiles.Insert(0, mainConfigJson);

            var configurationBuilder = new ConfigurationBuilder();
            // Add config json files
            foreach (var file in configFiles)
            {
                _log.Info($"Config: {file}");
                configurationBuilder.AddJsonFile(file);
            }

            // Add command-line arguments
            configurationBuilder.AddCommandLine(args);

            var configurationBuilderResult = configurationBuilder.Build();
            configurationBuilderResult.Bind(AppConfiguration.Instance);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exceptionStr = e.ExceptionObject.ToString();
            _log.Fatal(exceptionStr);
        }
    }
}
