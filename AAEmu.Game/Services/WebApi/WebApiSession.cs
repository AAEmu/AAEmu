using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NetCoreServer;
using NLog;

namespace AAEmu.Game.Services.WebApi;

public class WebApiSession : HttpSession
{
    private readonly WebApiServer _server;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public WebApiSession(WebApiServer server) : base(server)
    {
        _server = server;
    }

    protected override void OnReceivedRequest(HttpRequest request)
    {
        HttpResponse response = null;
        try
        {
            var (foundRoute, matches) = _server.RouteMapper.GetRoute(request.Url, new HttpMethod(request.Method));
            if (foundRoute is null)
            {
                response = new HttpResponse((int)HttpStatusCode.NotFound);
                response.SetBody("Not found");

                InternalSendResponseAsync(response);
                return;
            }

            List<object> parameters = [];
            foreach (var parameter in foundRoute.TargetMethod.GetParameters())
            {
                if (parameter.ParameterType == typeof(HttpRequest))
                {
                    parameters.Add(request);
                }
                else if (parameter.ParameterType == typeof(MatchCollection))
                {
                    parameters.Add(matches);
                }
            }

            object[] args = parameters.ToArray();

            var activate = Activator.CreateInstance(foundRoute.TargetMethod.DeclaringType);
            response = (HttpResponse)foundRoute.TargetMethod.Invoke(activate, args);
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

        InternalSendResponseAsync(response);
    }

    protected virtual void InternalSendResponseAsync(HttpResponse response)
    {
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
