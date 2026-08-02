using System.Net;

using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.World.Models;

using NLog;

namespace AAEmu.World.Core.Network;

public class ZoneNetwork : Singleton<ZoneNetwork>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private Server? _server;
    private readonly ZoneProtocolHandler _handler = new();

    public void Start(ZoneNetworkConfig config)
    {
        var host = config.Host.Equals("*", StringComparison.Ordinal)
            ? IPAddress.Any
            : IPAddress.Parse(config.Host);

        _server = new Server(host, config.Port, _handler);
        _server.Start();

        Logger.Info(
            "World Server listening for zone connections on {0}:{1}",
            config.Host == "*" ? "0.0.0.0" : config.Host,
            config.Port);
    }

    public void Stop()
    {
        if (_server?.IsStarted ?? false)
            _server.Stop();
    }
}
