using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAEmu.Login.Core.Network.Login;

public class LoginNetwork(
    ILoginProtocolHandler protocolHandler,
    IOptions<PublicNetworkConfig> publicNetworkConfig,
    ILogger<LoginNetwork> logger) : ILoginNetwork
{
    private Server? _server;

    public void Start()
    {
        var config = publicNetworkConfig.Value;
        _server = new Server(
            config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port, protocolHandler);
        _server.Start();

        logger.LogInformation("Network started with number of connections: {Connections}", config.NumConnections);
    }

    public void Stop()
    {
        if (_server is { IsStarted: true })
            _server.Stop();

        logger.LogInformation("Network stopped");
    }
}
