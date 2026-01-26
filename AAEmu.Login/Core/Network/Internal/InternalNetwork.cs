using System.Net;
using System.Net.Sockets;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AAEmu.Login.Core.Network.Internal;

public class InternalNetwork(
    IInternalProtocolHandler protocolHandler,
    IOptions<InternalNetworkConfig> internalNetworkConfig,
    ILogger<InternalNetwork> logger)
    : IInternalNetwork
{
    private Server? _server;

    public void Start()
    {
        var config = internalNetworkConfig.Value;
        var host =
            new IPEndPoint(config.Host.Equals("*") ? IPAddress.IPv6Any : IPAddress.Parse(config.Host), config.Port);

        _server = new Server(host.Address, host.Port, protocolHandler);
        _server.OptionDualMode = host.AddressFamily == AddressFamily.InterNetworkV6;
        _server.Start();

        logger.LogInformation("InternalNetwork started");
    }

    public void Stop()
    {
        if (_server?.IsStarted == true)
            _server.Stop();

        logger.LogInformation("InternalNetwork stopped");
    }
}
