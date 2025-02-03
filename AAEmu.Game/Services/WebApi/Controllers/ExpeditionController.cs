using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Services.WebApi.Models;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

internal class ExpeditionController : BaseController
{
    [WebApiGet("/api/expedition/list")]
    public HttpResponse List()
    {
        var list = ExpeditionManager.Instance.Expeditions.Select(x => new ExpeditionModel(
            (uint)x.Id, x.Name, x.OwnerId, x.OwnerName, (uint)x.MotherId, x.Created,
            (uint)x.Members.Count,
            (uint)x.Members.Count(member => member.IsOnline)
        ));

        return OkJson(list);
    }
}
