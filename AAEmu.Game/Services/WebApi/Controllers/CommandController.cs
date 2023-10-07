using AAEmu.Game.Core.Managers;
using System.Text.Json;
using System.Text.RegularExpressions;
using AAEmu.Game.Core.Managers.World;
using NetCoreServer;
using AAEmu.Game.Services.WebApi.Models;

#pragma warning disable CA1822 // Mark members as static

namespace AAEmu.Game.Services.WebApi.Controllers;

internal class CommandController : BaseController
{
    [WebApiGet("/command/([^/]+)")]
    public HttpResponse ExecuteCommand(HttpRequest request, MatchCollection matches)
    {
        var commandName = matches[0].Groups[1].Value;
        if (string.IsNullOrWhiteSpace(commandName))
            return BadRequestJson(new ErrorModel("Command name is required"));

        var jsonBody = JsonSerializer.Deserialize<JsonElement>(request.Body);
        var characterName = jsonBody.GetProperty("characterName").GetString();
        if (string.IsNullOrWhiteSpace(characterName))
            return BadRequestJson(new ErrorModel("Character name is required"));

        var commandLine = jsonBody.GetProperty("commandLine").GetString();
        if (string.IsNullOrWhiteSpace(commandLine))
            return BadRequestJson(new ErrorModel("Command line is required"));

        commandLine = $"/{commandName} {jsonBody.GetProperty("commandLine").GetString()}";

        var character = WorldManager.Instance.GetCharacter(characterName);
        if (character == null)
            return BadRequestJson(new ErrorModel($"Character \"{characterName}\" not found"));

        CommandManager.Instance.Handle(character, commandLine, out var messageOutput);

        var commandResult = new
        {
            commandName,
            commandLine,
            characterName,
            messageOutput.Messages,
            messageOutput.ErrorMessages
        };

        return OkJson(commandResult);
    }
}
