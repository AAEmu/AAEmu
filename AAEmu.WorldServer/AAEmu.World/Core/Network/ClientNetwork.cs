using System.Net;

using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.World.Models;

using NLog;

namespace AAEmu.World.Core.Network;

/// <summary>Phase 3 stub — listens for game clients (default port 1239).</summary>
public class ClientNetwork : Singleton<ClientNetwork>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private Server? _server;
    private readonly ClientProtocolHandler _handler = new();

    public void Start(ClientNetworkConfig config)
    {
        var host = config.Host.Equals("*", StringComparison.Ordinal)
            ? IPAddress.Any
            : IPAddress.Parse(config.Host);

        _server = new Server(host, config.Port, _handler);
        _server.Start();
        Logger.Info("World client stub listening on {0}:{1}", host, config.Port);
    }

    public void Stop()
    {
        if (_server?.IsStarted ?? false)
            _server.Stop();
    }
}
