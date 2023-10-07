using System.Net;
using System.Net.Sockets;
using NetCoreServer;
using NLog;

namespace AAEmu.Game.Services.WebApi;

public class WebApiServer : HttpServer
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    internal RouteMapper RouteMapper { get; private set; }

    public WebApiServer(IPAddress address, int port) : base(address, port)
    {
        RegisterRoutes();
    }

    public WebApiServer(IPAddress address, int port, RouteMapper routeMapper) : base(address, port)
    {
        RouteMapper = routeMapper;
    }

    private void RegisterRoutes()
    {
        RouteMapper = new RouteMapper();
        RouteMapper.DiscoverRoutes<IController>();
    }

    protected override TcpSession CreateSession() => new WebApiSession(this);

    protected override void OnError(SocketError error)
    {
        Logger.Warn($"WebApi server caught an error with code {error}");
    }
}
