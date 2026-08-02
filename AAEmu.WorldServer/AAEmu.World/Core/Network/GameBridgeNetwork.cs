using System.Net;

using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.World.Models;

using NLog;

namespace AAEmu.World.Core.Network;

/// <summary>Listens for AAEmu.Game push of player enter/leave (default :1241).</summary>
public class GameBridgeNetwork : Singleton<GameBridgeNetwork>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private Server? _server;
    private readonly GameBridgeProtocolHandler _handler = new();

    public void Start(GameBridgeNetworkConfig config)
    {
        if (!config.Enabled)
        {
            Logger.Info("Game bridge disabled");
            return;
        }

        var host = config.Host.Equals("*", StringComparison.Ordinal)
            ? IPAddress.Any
            : IPAddress.Parse(config.Host);

        _server = new Server(host, config.Port, _handler);
        _server.Start();
        Logger.Info("Game bridge listening on {0}:{1}", host, config.Port);
    }

    public void Stop()
    {
        if (_server?.IsStarted ?? false)
            _server.Stop();
    }
}
