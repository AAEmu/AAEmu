﻿using System.Linq;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Services.WebApi.Models;
using NetCoreServer;

namespace AAEmu.Game.Services.WebApi.Controllers;

#pragma warning disable CA1822 // Mark members as static

/// <summary>
/// Character controller for the WebApi
/// </summary>6
internal class WorldController : BaseController
{
    [WebApiGet("/world/logged-characters")]
    public HttpResponse GetCharacter(HttpRequest request)
    {
        var loggedCharacters = WorldManager.Instance.GetAllCharacters().Select(x => new CharacterModel(x.Id, x.Name));
        return OkJson(loggedCharacters);
    }
}

