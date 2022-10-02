using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils.DB;
using AAEmu.Login.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NLog;
using NLog.Config;

namespace AAEmu.Login
{
    public static class Program
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private static Thread _thread = Thread.CurrentThread;
        private static DateTime _startTime;
        private static string Name => Assembly.GetExecutingAssembly().GetName().Name;
        private static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "???";

        public static int UpTime => (int) (DateTime.UtcNow - _startTime).TotalSeconds;

        public static async Task Main(string[] args)
        {
            Initialization();

            var mainConfig = Path.Combine(FileManager.AppPath, "Config.json");
            if (File.Exists(mainConfig))
                Configuration(args, mainConfig);
            else
            {
                _log.Fatal($"{mainConfig} doesn't exist!");
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
                    services.AddSingleton<IHostedService, LoginService>();
                });

            await builder.RunConsoleAsync();
        }

        private static void Initialization()
        {
            _thread.Name = "AA.LoginServer Base Thread";
            _startTime = DateTime.UtcNow;
        }

        private static void Configuration(string[] args, string mainConfigJson)
        {
            var configJsonFile = Path.Combine(FileManager.AppPath, "Config.json");
            var configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile(mainConfigJson)
                .AddCommandLine(args)
                .Build();

            configurationBuilder.Bind(AppConfiguration.Instance);

            LogManager.ThrowConfigExceptions = false;
            LogManager.Configuration = new XmlLoggingConfiguration(Path.Combine(FileManager.AppPath, "NLog.config"));
        }
    }
}
