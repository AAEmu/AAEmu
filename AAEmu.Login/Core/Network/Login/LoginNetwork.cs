using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Models;
using NLog;

namespace AAEmu.Login.Core.Network.Login;

public class LoginNetwork(ILoginProtocolHandler protocolHandler) : ILoginNetwork
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Server? _server;

    public void Start()
    {
        var config = AppConfiguration.Instance.Network;
        _server = new Server(
            config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port, protocolHandler);
        _server.Start();

        Logger.Info("Network started with Number of Connections: " + config.NumConnections);
    }

    public void Stop()
    {
        if (_server is { IsStarted: true })
            _server.Stop();

        Logger.Info("Network stopped");
    }
}
