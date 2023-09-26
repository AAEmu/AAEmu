using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

/// <summary>
/// Status controller for the WebApi
/// </summary>6
internal class StatusController : BaseController
{
    [WebApiGet("/status")]
    public HttpResponse GetStatus(HttpRequest request)
    {
        var playerCount = WorldManager.Instance.GetAllCharacters().Count;
        var serverUptime = new TimeSpan(0, 0, Program.UpTime);

        var responseBody = @$"
Server uptime: {serverUptime}<br/>
Players online: {playerCount}<br/>
Number of TaskManager Executing Jobs: {TaskManager.Instance.GetExecutingJobsCount().GetAwaiter().GetResult()}<br/>
Total number of Schedule Requests: {TaskManager.Instance.ScheduleRequestCount}";

        return OkHtml(responseBody);
    }
}
