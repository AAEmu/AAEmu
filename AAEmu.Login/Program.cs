using System.Configuration;
using System.Net;
using System.Reflection;
using AAEmu.Commons.IO;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.PacketHandlers;
using AAEmu.Login.Core.Services;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MySql.Data.MySqlClient;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OSVersionExtension;

namespace AAEmu.Login;

public static class Program
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static readonly Thread _thread = Thread.CurrentThread;
    private static DateTime _startTime;
    private static string Name => Assembly.GetExecutingAssembly().GetName().Name ?? "AAEmu.Login";
    private static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "???";

    public static int UpTime => (int)(DateTime.UtcNow - _startTime).TotalSeconds;

    public static async Task Main(string[] args)
    {
        Initialization();

        LoadConfiguration();

        var builder = WebApplication.CreateSlimBuilder(args);

        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            if (context.Configuration.GetRequiredSection(PublicNetworkConfig.ConfigurationSectionName)
                    .Get<PublicNetworkConfig>() is not { } publicNetworkConfig)
            {
                throw new ConfigurationErrorsException("Could not load public network configuration");
            }

            // Set up Kestrel to listen on the public network interface for incoming connections from game clients
            options.Listen(publicNetworkConfig.Host == "*"
                    ? IPAddress.Any
                    : IPAddress.Parse(publicNetworkConfig.Host),
                publicNetworkConfig.Port,
                opts => opts.UseConnectionHandler<LoginConnectionHandler>());

            // Listen on any HTTP URLs assigned by the orchestrator (e.g. Aspire via ASPNETCORE_URLS)
            var urls = context.Configuration[WebHostDefaults.ServerUrlsKey];
            if (urls is not null)
            {
                foreach (var url in urls.Split(';'))
                {
                    var uri = new Uri(url);
                    options.ListenAnyIP(uri.Port);
                }
            }
        });

        builder.Configuration
            .AddJsonFile(Path.Combine(FileManager.AppPath, "Config.json"), optional: true, reloadOnChange: true)
            .AddJsonFile(Path.Combine(FileManager.AppPath, "Config.Local.json"), optional: true, reloadOnChange: true)
            .AddUserSecrets<LoginService>()
            .AddEnvironmentVariables()
            .AddCommandLine(args);

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        // Configure services
        builder.Logging.ClearProviders()
            .AddNLog();

        if (otlpEndpoint is not null)
        {
            builder.Services.AddOpenTelemetry()
                .WithLogging(null, options =>
                {
                    options.IncludeFormattedMessage = true;
                    options.IncludeScopes = true;
                })
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation(options =>
                        options.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health")))
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation())
                .UseOtlpExporter();
        }
        builder.Services.AddOptions();
        builder.Services.AddOptionsWithValidateOnStart<AppConfiguration>()
            .BindConfiguration("")
            .ValidateDataAnnotations()
            .Validate(
                config => config.CharacterSlots.AvailableSlots <= config.CharacterSlots.MaxCountLimit &&
                          config.CharacterSlots.CountLimit <= config.CharacterSlots.MaxCountLimit &&
                          config.CharacterSlots.WorldLimit <= config.CharacterSlots.MaxCountLimit,
                "CharacterSlots.AvailableSlots, CountLimit, and WorldLimit must not exceed MaxCountLimit.")
            .Validate(
                config => config.WorldListRequestTimeout > TimeSpan.Zero,
                "WorldListRequestTimeout must be greater than zero.");
        builder.Services.AddOptionsWithValidateOnStart<DBConnectionsConfig>()
            .BindConfiguration(DBConnectionsConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();
        builder.Services.AddOptionsWithValidateOnStart<InternalNetworkConfig>()
            .BindConfiguration(InternalNetworkConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();
        builder.Services.AddOptionsWithValidateOnStart<PublicNetworkConfig>()
            .BindConfiguration(PublicNetworkConfig.ConfigurationSectionName)
            .ValidateDataAnnotations();

        builder.Services.AddSingleton<IMySqlConnectionFactory, MySqlConnectionFactory>();
        builder.Services.AddTransient<MySqlConnection>(sp =>
            sp.GetRequiredService<IMySqlConnectionFactory>().CreateConnection());

        builder.Services.AddHostedService<MySqlInitializer>();
        builder.Services.AddHostedService<LoginService>();

        builder.Services.AddSingleton<IGameController, GameController>();
        builder.Services.AddSingleton<ILoginController, LoginController>();
        builder.Services.AddSingleton<IRequestController, RequestController>();

        builder.Services.AddPasswordAuth();
        builder.Services.AddKoreaAuth();

        builder.Services.AddInternalNetwork();
        builder.Services.AddLoginNetwork();

        builder.Services.AddInternalPacketHandlers();
        builder.Services.AddLoginPacketHandlers();

        builder.Services.AddHealthChecks();

        var app = builder.Build();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready");

        await app.RunAsync();
    }

    private static void LoadConfiguration()
    {
        LogManager.ThrowConfigExceptions = false;
        LogManager.Configuration = new XmlLoggingConfiguration(Path.Combine(FileManager.AppPath, "NLog.config"));
    }

    /// <summary>
    /// Tries to return a more human-readable OS name
    /// </summary>
    /// <returns></returns>
    private static string GetOsName()
    {
        try
        {
            // Note: This NuGet package can throw a exception in some cases, so we try to catch it
            return OSVersion.GetOperatingSystem().ToString();
        }
        catch
        {
            return "Unknown";
        }
    }

    private static void Initialization()
    {
        _thread.Name = "AA.LoginServer Base Thread";
        _startTime = DateTime.UtcNow;
        Logger.Info($"{Name} version {Version}");
        Logger.Info(
            $"Running as {(Environment.Is64BitProcess ? "64" : "32")}-bits on {(Environment.Is64BitOperatingSystem ? "64" : "32")}-bits {GetOsName()} ({Environment.OSVersion})");
        if (!Environment.Is64BitProcess)
        {
            Logger.Warn($"Running in 32-bits mode is not recommended due to memory constraints");
        }
    }
}
