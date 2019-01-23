using System;
using System.Reflection;
using System.Threading;
using AAEmu.Commons.IO;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;
using Microsoft.Extensions.Configuration;
using NLog;
using NLog.Config;

namespace AAEmu.Login
{
    public static class Program
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private static Thread _thread = Thread.CurrentThread;
        private static bool _shutdown;
        private static DateTime _startTime;
        private static string Name => Assembly.GetExecutingAssembly().GetName().Name;
        private static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString();
        private static AutoResetEvent _signal = new AutoResetEvent(false);

        public static int UpTime => (int) (DateTime.Now - _startTime).TotalSeconds;

        public static void Main(string[] args)
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

            GameController.Instance.Load();
            LoginNetwork.Instance.Start();
            InternalNetwork.Instance.Start();

            _signal.WaitOne();

            LoginNetwork.Instance.Stop();
            InternalNetwork.Instance.Stop();

            LogManager.Flush();
        }

        private static void Initialization()
        {
            _thread.Name = "AA.LoginServer Base Thread";
            _startTime = DateTime.Now;
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.IsTerminating)
                {
                    _log.Fatal((Exception) e.ExceptionObject);
                    Shutdown();
                }
                else
                {
                    _log.Error((Exception) e.ExceptionObject);
                }
            };
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => Shutdown();
            AppDomain.CurrentDomain.DomainUnload += (sender, e) => Shutdown();
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

        public static void Shutdown()
        {
            if (_shutdown)
                return;
            _shutdown = true;
            _signal.Set();
        }
    }
}
