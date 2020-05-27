using System;
using System.Threading.Tasks;
using AAEmu.Commons.Configuration.Server;
using AAEmu.Commons.DI;
using AAEmu.Commons.IO;
using AAEmu.Login.Models;
using AAEmu.Login.Network.Message;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using SlimMessageBus;

namespace AAEmu.Login
{
   public static class Program
    {
#if DEBUG
        private const string Title = "AAEmu: Authentication Server (DEBUG)";
#else
        private const string Title = "AAEmu: Authentication Server (RELEASE)";
#endif

        public static async Task Main(string[] args)
        {
            Console.Title = Title;
            var builder = new HostBuilder()
                .ConfigureHostConfiguration(configBuilder =>
                {
                    configBuilder.SetBasePath(FileManager.AppPath);
                    configBuilder.AddEnvironmentVariables(prefix: "EMU_");
                    configBuilder.AddCommandLine(args);
                })
                .ConfigureAppConfiguration((context, configBuilder) =>
                {
                    configBuilder.SetBasePath(FileManager.AppPath);
                    configBuilder.AddEnvironmentVariables(prefix: "EMU_");
                    configBuilder.AddJsonFile($"Config.json", true, true);
                    configBuilder.AddJsonFile($"Config.{context.HostingEnvironment.EnvironmentName}.json", true,
                        true);
                    configBuilder.AddCommandLine(args);
                })
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration.Get<AuthServerConfiguration>();

                    services.AddLogging();
                    services.AddHostedService<LoginService>();
                    services.Scan(scan => scan
                        .FromExecutingAssembly()
                        .FromApplicationDependencies()
                        .AddClasses(classes => classes.AssignableTo<ITransientService>())
                            .AsImplementedInterfaces()
                            .WithTransientLifetime()
                        .AddClasses(classes => classes.AssignableTo<IScopedService>())
                            .As<IScopedService>()
                            .WithScopedLifetime()
                        .AddClasses(classes => classes.AssignableTo<ISingletonService>())
                            .AsImplementedInterfaces()
                            .WithSingletonLifetime()
                        .AddClasses(classes => classes.AssignableTo(typeof(IRequestHandler<,>)))
                            .AsSelf()
                            .WithTransientLifetime()
                        .AddClasses(classes => classes.AssignableTo<ILoginHandler>())
                            .AsSelf()
                            .WithTransientLifetime()
                    );

                    services.AddDbContext<AuthContext>(options =>
                        options.UseNpgsql(configuration.ConnectionStrings.PostgresConnection)
                    );

                    services.AddSingleton(MessageBus.Build);
                })
                .ConfigureLogging((context, logger) =>
                {
                    logger.ClearProviders();
                    logger.AddNLog("nlog.config");
#if DEBUG
                    logger.SetMinimumLevel(LogLevel.Debug);
#endif
                });

            var host = builder.Build();
            await host.RunAsync();
        }
    }
}
