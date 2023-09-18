using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;
using Microsoft.Extensions.Hosting;
using NetCoreServer;
using NLog;

namespace AAEmu.Game.Services;

public class WebApiService : IHostedService
{
    private WebApiServer _server;
    private static Logger _log = LogManager.GetCurrentClassLogger();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var config = AppConfiguration.Instance.WebApiNetwork;
        if (config is null)
        {
            _log.Warn("WebApi server configuration not found. WebApi will not start");
            return Task.CompletedTask;
        }

        _server = new WebApiServer(config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port);
        _server.Start();

        _log.Info($"WebApi server started on {config.Host}:{config.Port}");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server?.IsStarted ?? false)
            _server.Stop();

        _log.Info("WebApi server stopped");
        return Task.CompletedTask;
    }
}

public class WebApiServer : HttpServer
{
    public WebApiServer(IPAddress address, int port) : base(address, port)
    {
    }

    protected override void OnConnected(TcpSession session)
    {
        var httpSession = (HttpSession)session;

        byte[] buffer = new byte[4096];
        long bytesRead = httpSession.Receive(buffer);
        string requestData = Encoding.UTF8.GetString(buffer, 0, (int)bytesRead);

        // Now parse requestData to extract headers and payload
        // This is simplified and will be improved later
        string[] lines = requestData.Split(new string[] { "\r\n" }, StringSplitOptions.None);
        string method = lines[0].Split(' ')[0];  // Method
        string urlPath = lines[0].Split(' ')[1];     // Path

        string content = @$"
Method: {method}<br/>
Path: {urlPath}<br/>
Number of TaskManager Executing Jobs: {TaskManager.Instance.GetExecutingJobsCount().GetAwaiter().GetResult()}<br/>
Total number of Schedule Requests: {TaskManager.Instance.ScheduleRequestCount}";

        string httpResponse = $"HTTP/1.1 200 OK\r\nContent-Length: {content.Length}\r\n\r\n{content}";

        httpSession.Send(httpResponse);
        httpSession.Disconnect();
    }
}
