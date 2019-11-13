using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.IO;
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

            using (var ctx = new GameDBContext())
            {
                /*ctx.Abilities.ToList();
                ctx.Actabilities.ToList();
                ctx.Appellations.ToList();
                ctx.Blocked.ToList();
                ctx.CashShopItem.ToList();
                ctx.Characters.ToList();
                ctx.CompletedQuests.ToList();
                ctx.ExpeditionMembers.ToList();
                ctx.ExpeditionRolePolicies.ToList();
                ctx.Expeditions.ToList();
                ctx.FamilyMembers.ToList();
                ctx.Friends.ToList();
                ctx.Housings.ToList();
                ctx.Items.ToList();
                ctx.Mates.ToList();
                ctx.Options.ToList();
                ctx.PortalBookCoords.ToList();
                ctx.PortalVisitedDistrict.ToList();
                ctx.Quests.ToList();
                ctx.Skills.ToList();*/

                try
                {
                    ctx.ThrowIfNotExists();
                }
                catch (Exception e)
                {
                    _log.Error("Error on DB connect: {0}", (e.InnerException ?? e).Message);
                    LogManager.Flush();
                    return;
                }
            }

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
    }
}
