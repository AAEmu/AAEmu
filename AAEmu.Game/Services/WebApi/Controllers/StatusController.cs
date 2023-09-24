using System;
using System.Net;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

#pragma warning disable CA1822 // Mark members as static

/// <summary>
/// Status controller for the WebApi
/// </summary>6
internal class StatusController : IController
{
    [WebApiGet("/status")]
    public HttpResponse GetStatus(HttpRequest request)
    {
        var response = new HttpResponse((int)HttpStatusCode.OK);
        response.SetContentType("text/html");

        var playerCount = WorldManager.Instance.GetAllCharacters().Count;
        var serverUptime = new TimeSpan(0, 0, Program.UpTime);

        var responseBody = @$"
Server uptime: {serverUptime}<br/>
Players online: {playerCount}<br/>
Number of TaskManager Executing Jobs: {TaskManager.Instance.GetExecutingJobsCount().GetAwaiter().GetResult()}<br/>
Total number of Schedule Requests: {TaskManager.Instance.ScheduleRequestCount}";

        response.SetBody(responseBody);
        return response;
    }

    [WebApiPost("/status")]
    public HttpResponse PostStatus(HttpRequest request)
    {
        var response = new HttpResponse((int)HttpStatusCode.MethodNotAllowed);
        response.SetContentType("text/html");

        var playerCount = WorldManager.Instance.GetAllCharacters().Count;
        var serverUptime = new TimeSpan(0, 0, Program.UpTime);

        var responseBody = @$"POST";

        response.SetBody(responseBody);
        return response;
    }
}
