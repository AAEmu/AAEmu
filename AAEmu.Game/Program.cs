using System;
using System.Reflection;
using System.Threading;
using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Network.Login;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Utils.Scripts;
using Microsoft.Extensions.Configuration;
using NLog;
using NLog.Config;

namespace AAEmu.Game
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

            Test();

            var connection = MySQL.CreateConnection();
            if (connection == null)
            {
                LogManager.Flush();
                return;
            }

            connection.Close();

            TaskIdManager.Instance.Initialize();
            TaskManager.Instance.Initialize();

            ObjectIdManager.Instance.Initialize();

            ItemIdManager.Instance.Initialize();
            CharacterIdManager.Instance.Initialize();
            FamilyIdManager.Instance.Initialize();
            VisitedSubZoneIdManager.Instance.Initialize();
            PrivateBookIdManager.Instance.Initialize();

            ZoneManager.Instance.Load();
            WorldManager.Instance.Load();
            QuestManager.Instance.Load();

            FormulaManager.Instance.Load();
            ExpirienceManager.Instance.Load();

            TlIdManager.Instance.Initialize();
            ItemManager.Instance.Load();
            PlotManager.Instance.Load();
            SkillManager.Instance.Load();
            CraftManager.Instance.Load();
            HousingManager.Instance.Load();

            NameManager.Instance.Load();
            FactionManager.Instance.Load();
            CharacterManager.Instance.Load();
            FamilyManager.Instance.Load();
            PortalManager.Instance.Load();

            NpcManager.Instance.Load();
            DoodadManager.Instance.Load();

            SpawnManager.Instance.Load();
            SpawnManager.Instance.SpawnAll();
            ScriptCompiler.Compile();

            TimeManager.Instance.Start();
            TaskManager.Instance.Start();
            GameNetwork.Instance.Start();
            StreamNetwork.Instance.Start();
            LoginNetwork.Instance.Start();

            _signal.WaitOne();

            SpawnManager.Instance.Stop();
            TaskManager.Instance.Stop();
            GameNetwork.Instance.Stop();
            StreamNetwork.Instance.Stop();
            LoginNetwork.Instance.Stop();
            LogManager.Flush();
        }

        private static void Initialization()
        {
            _thread.Name = "AA.Game Base Thread";
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

        public static void Test()
        {
        }
    }
}
