using AAEmu.Game.Core.Managers.World;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

internal class CommandController : BaseController
{
    [WebApiGet("/command/([^/]+)")]
    public HttpResponse GetOnline(HttpRequest request)
    {
        var online = WorldManager.Instance.GetAllCharacters().Count;
        return OkJson(online);
    }
}
