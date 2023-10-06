using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using NetCoreServer;
using NLog;

namespace AAEmu.Game.Services.WebApi;

public class WebApiSession : HttpSession
{
    private static Logger Logger = LogManager.GetCurrentClassLogger();
    public WebApiSession(HttpServer server) : base(server)
    {
    }

    protected override void OnReceivedRequest(HttpRequest request)
    {
        HttpResponse response = null;
        try
        {
            response = RouteMapper.GetRoute(request.Url, new HttpMethod(request.Method))?.Invoke(request);

            if (response is null)
            {
                response = new HttpResponse((int)HttpStatusCode.NotFound);
                response.SetBody("Not found");
            }
        }
        catch (Exception e)
        {
            Logger.Error(e);
            response = new HttpResponse((int)HttpStatusCode.InternalServerError);
            response.SetContentType("text/html");
            var htmlError =
                @$"<h1>Internal server error</h1>
                   <h3>Error:{e.Message}</h3>
                   <h2>Stack trace:</h3>
                   <pre>{e.StackTrace}</pre>";

            response.SetBody(htmlError);
        }

        SendResponseAsync(response);
    }
    protected override void OnReceivedRequestError(HttpRequest request, string error)
    {
        Logger.Warn($"Request error: {error}");
    }

    protected override void OnError(SocketError error)
    {
        Logger.Warn($"HTTP session caught an error: {error}");
    }
}
