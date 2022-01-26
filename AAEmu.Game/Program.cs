using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.IO;
using AAEmu.Game.Genesis;
using AAEmu.Game.Models;
using AAEmu.Game.Utils.DB;
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
            LoadConfiguration();

            _log.Info("{0} version {1}", Name, Version);

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
            
            await builder.RunConsoleAsync();
        }
        
        private static void Initialization()
        {
            _thread.Name = "AA.Game Base Thread";
            _startTime = DateTime.UtcNow;
        }

        public static void LoadConfiguration()
        {
            var mainConfig = Path.Combine(FileManager.AppPath, "Config.json");
            if (File.Exists(mainConfig))
                Configuration(_launchArgs, mainConfig);
            else
            {
                _log.Error($"{mainConfig} doesn't exist!");
                return;
            }
        }

        private static void Configuration(string[] args, string mainConfigJson)
        {
            // Load NLog configuration
            LogManager.Configuration = new XmlLoggingConfiguration(Path.Combine(FileManager.AppPath, "NLog.config"), false);

            // Load Game server configuration
            // Get files inside in the Configurations folder
            var configFiles = Directory.GetFiles(Path.Combine(FileManager.AppPath,"Configurations"), "*.json", SearchOption.AllDirectories).ToList();
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
        
        private static void OnUnhandledException(
            object sender, UnhandledExceptionEventArgs e)
        {
            var exceptionStr = e.ExceptionObject.ToString();
            //_log.Error(exceptionStr);
            _log.Fatal(exceptionStr);
        }
    }
}
