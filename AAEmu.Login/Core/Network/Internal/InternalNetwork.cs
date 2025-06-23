using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Models;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Login.Core.Network.Internal;

public class InternalNetwork(IInternalProtocolHandler protocolHandler, IOptions<AppConfiguration> appConfig)
    : IInternalNetwork
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Server? _server;

    public void Start()
    {
        var config = appConfig.Value.InternalNetwork;
        var host =
            new IPEndPoint(config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port);

        _server = new Server(host.Address, host.Port, protocolHandler);
        _server.Start();

        Logger.Info("InternalNetwork started");
    }

    public void Stop()
    {
        if (_server?.IsStarted == true)
            _server.Stop();

        Logger.Info("InternalNetwork stoped");
    }
}
