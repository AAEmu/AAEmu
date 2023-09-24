using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using NetCoreServer;
using NLog;

namespace AAEmu.Game.Services.WebApi;

public class WebApiSession : HttpSession
{
    private static Logger _log = LogManager.GetCurrentClassLogger();
    public WebApiSession(HttpServer server) : base(server)
    {
    }

    protected override void OnReceivedRequest(HttpRequest request)
    {
        var response = RouteMapper.GetRoute(request.Url, new HttpMethod(request.Method))?.Invoke();

        if (response is null)
        {
            response = new HttpResponse((int)HttpStatusCode.NotFound);
            response.SetBody("Not found");
        }

        SendResponseAsync(response);
    }
    protected override void OnReceivedRequestError(HttpRequest request, string error)
    {
        _log.Warn($"Request error: {error}");
    }

    protected override void OnError(SocketError error)
    {
        _log.Warn($"HTTP session caught an error: {error}");
    }
}
