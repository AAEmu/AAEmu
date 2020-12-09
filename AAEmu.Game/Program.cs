using System;
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
        private static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString();
        public static AutoResetEvent ShutdownSignal = new AutoResetEvent(false); // TODO save to shutdown server?

        public static int UpTime => (int)(DateTime.Now - _startTime).TotalSeconds;

        public static async Task Main(string[] args)
        {
            Initialization();
            
            if (FileManager.FileExists(FileManager.AppPath + "Config.json"))
                Configuration(args);
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
        
        private static void OnUnhandledException(
            object sender, UnhandledExceptionEventArgs e)
        {
            var exceptionStr = e.ExceptionObject.ToString();
            _log.Error(exceptionStr);
            _log.Fatal(exceptionStr);
        }
    }
}
