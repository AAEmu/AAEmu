using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
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
        private static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString();
        public static AutoResetEvent ShutdownSignal = new AutoResetEvent(false); // TODO save to shutdown server?

        public static int UpTime => (int)(DateTime.Now - _startTime).TotalSeconds;

        public static async Task Main(string[] args)
        {
            CliUtil.WriteHeader("Game & Stream", ConsoleColor.DarkGreen);
            CliUtil.LoadingTitle();

            Initialization();

            if (FileManager.FileExists(FileManager.AppPath + "Config.json"))
            {
                Configuration(args);
            }
            else
            {
                _log.Error($"{FileManager.AppPath}Config.json doesn't exist!");
                return;
            }

            _log.Info("{0} version {1}", Name, Version);

            var connection = MySQL.CreateConnection();
            if (connection == null)
            {
                LogManager.Flush();
                return;
            }

            connection.Close();

            // Check if there are any updates
            CheckDatabaseUpdates();


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
                });

            await builder.RunConsoleAsync();
        }

        private static void Initialization()
        {
            _thread.Name = "AA.Game Base Thread";
            _startTime = DateTime.Now;
        }

        private static void Configuration(string[] args)
        {
            var configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile(FileManager.AppPath + "Config.json")
                .AddCommandLine(args)
                .Build();

            configurationBuilder.Bind(AppConfiguration.Instance);

            LogManager.Configuration = new XmlLoggingConfiguration(FileManager.AppPath + "NLog.config", false);
        }

        private static void CheckDatabaseUpdates()
        {
            _log.Info("Checking for updates...");
            var files = Directory.GetFiles("sql");
            foreach (var filePath in files.Where(file => Path.GetExtension(file).ToLower() == ".sql"))
            {
                RunUpdate(Path.GetFileName(filePath));
            }
        }

        private static void RunUpdate(string updateFile)
        {
            if (UpdatesManager.Instance.CheckUpdate(updateFile))
            {
                return;
            }
            _log.Info("Update '{0}' found, executing...", updateFile);
            UpdatesManager.Instance.RunUpdate(updateFile);
        }
    }
}
