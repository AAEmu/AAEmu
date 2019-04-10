using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.IO;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;
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
        private static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString();

        public static int UpTime => (int) (DateTime.Now - _startTime).TotalSeconds;

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

            var connection = MySQL.Create();
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
    }
}
