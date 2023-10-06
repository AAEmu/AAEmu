using System.Net;
using System.Net.Sockets;
using NetCoreServer;
using NLog;

namespace AAEmu.Game.Services.WebApi;

public class WebApiServer : HttpServer
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public WebApiServer(IPAddress address, int port) : base(address, port)
    {
        RegisterRoutes();
    }

    private static void RegisterRoutes()
    {
        RouteMapper.DiscoverRoutes<IController>();
    }

    protected override TcpSession CreateSession() => new WebApiSession(this);

    protected override void OnError(SocketError error)
    {
        Logger.Warn($"WebApi server caught an error with code {error}");
    }
}
