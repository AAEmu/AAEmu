﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Login.Models;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NLog;
using NLog.Config;

namespace AAEmu.Login;

public static class Program
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static Thread _thread = Thread.CurrentThread;
    private static DateTime _startTime;
    private static string Name => Assembly.GetExecutingAssembly().GetName().Name;
    private static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "???";

    public static int UpTime => (int)(DateTime.UtcNow - _startTime).TotalSeconds;

    public static async Task Main(string[] args)
    {
        CliUtil.WriteHeader("Login", ConsoleColor.DarkGreen);
        CliUtil.LoadingTitle();
        Initialization();

        LoadConfiguration(args);

        Logger.Info($"{Name} version {Version}");

        // Apply MySQL Configuration
        MySQL.SetConfiguration(AppConfiguration.Instance.Connections.MySQLProvider);

        try
        {
            // Test the DB connection
            var connection = MySQL.CreateConnection();
            connection.Close();
            connection.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex, "MySQL connection failed, check your configuration!");
            LogManager.Flush();
            return;
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
                services.AddSingleton<IHostedService, LoginService>();
            });

        await builder.RunConsoleAsync();
    }

    private static bool LoadConfiguration(string[] args)
    {

        var mainConfig = Path.Combine(FileManager.AppPath, "Config.json");
        if (!File.Exists(mainConfig))
        {
            // If user secrets are defined the configuration file is not required
            var isUserSecretsDefined = IsUserSecretsDefined();
            if (!isUserSecretsDefined)
            {
                Logger.Fatal($"{mainConfig} doesn't exist!");
                return false;
            }

            //return false;
            mainConfig = null;
        }

        Configuration(args, mainConfig);
        return true;
    }

    private static void Initialization()
    {
        _thread.Name = "AA.LoginServer Base Thread";
        _startTime = DateTime.UtcNow;
    }

    private static void Configuration(string[] args, string mainConfigJson)
    {
        var configJsonFile = Path.Combine(FileManager.AppPath, "Config.json");
        var configurationBuilder = new ConfigurationBuilder();
        if (mainConfigJson != null)
        {
            configurationBuilder.AddJsonFile(mainConfigJson);
        }

        configurationBuilder
            .AddUserSecrets<LoginService>()
            .AddCommandLine(args);

        var configuration = configurationBuilder.Build();

        configuration.Bind(AppConfiguration.Instance);

        LogManager.ThrowConfigExceptions = false;
        LogManager.Configuration = new XmlLoggingConfiguration(Path.Combine(FileManager.AppPath, "NLog.config"));
    }

    private static bool IsUserSecretsDefined()
    {
        // Check if user secrets are defined
        var config = new ConfigurationBuilder()
            .AddUserSecrets<LoginService>()
            .Build();

        bool userSecretsDefined = config.AsEnumerable().Any();
        return userSecretsDefined;
    }
}
