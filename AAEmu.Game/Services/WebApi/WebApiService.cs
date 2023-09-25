using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Game.Models;
using Microsoft.Extensions.Hosting;
using NLog;

namespace AAEmu.Game.Services.WebApi;

public class WebApiService : IHostedService
{
    private WebApiServer _server;
    private static Logger _log = LogManager.GetCurrentClassLogger();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var config = AppConfiguration.Instance.WebApiNetwork;
        if (config is null)
        {
            _log.Warn("WebApi server configuration not found. WebApi will not start");
            return Task.CompletedTask;
        }

        _server = new WebApiServer(config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port);
        _server.Start();

        _log.Info($"WebApi server started on {config.Host}:{config.Port}");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server?.IsStarted ?? false)
            _server.Stop();

        _log.Info("WebApi server stopped");
        return Task.CompletedTask;
    }
}
