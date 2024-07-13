using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

#pragma warning disable CA1822 // Mark members as static

/// <summary>
/// Status controller for the WebApi
/// </summary>
internal class StatusController : BaseController
{
    [WebApiGet("/status")]
    public HttpResponse GetStatus(HttpRequest request)
    {
        var playerCount = WorldManager.Instance.GetAllCharacters().Count;
        var serverUptime = new TimeSpan(0, 0, Program.UpTime);
        var responseBody = $"Server uptime: {serverUptime}<br/>" +
                           $"Players online: {playerCount}<br/>"+
                           $"Number of TaskManager Jobs: {TaskManager.Instance.GetQueueCount()}<br/>";

        return OkHtml(responseBody);
    }
}
