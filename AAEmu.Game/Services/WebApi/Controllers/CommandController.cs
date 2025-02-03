using AAEmu.Game.Core.Managers;
using System.Text.Json;
using System.Text.RegularExpressions;
using AAEmu.Game.Core.Managers.World;
using NetCoreServer;
using AAEmu.Game.Services.WebApi.Models;

namespace AAEmu.Game.Services.WebApi.Controllers;

internal class CommandController : BaseController
{
    [WebApiPost("/api/commands/([^/]+)")]
    public HttpResponse ExecuteCommand(HttpRequest request, MatchCollection matches)
    {
        var commandName = matches[0].Groups[1].Value;
        if (string.IsNullOrWhiteSpace(commandName))
            return BadRequestJson(new ErrorModel("Command name is required"));

        var jsonBody = JsonSerializer.Deserialize<JsonElement>(request.Body);
        var commandCharacter = jsonBody.GetProperty("character").GetString();
        if (string.IsNullOrWhiteSpace(commandCharacter))
            return BadRequestJson(new ErrorModel("Character name is required"));

        var commandArguments = jsonBody.GetProperty("arguments").GetString();
        if (string.IsNullOrWhiteSpace(commandArguments))
            return BadRequestJson(new ErrorModel("Command line is required"));

        var commandLine = $"{commandName} {commandArguments}";

        var character = WorldManager.Instance.GetCharacter(commandCharacter);
        if (character == null)
            return BadRequestJson(new ErrorModel($"Character \"{commandCharacter}\" not found"));

        CommandManager.Instance.Handle(character, commandLine, out var messageOutput);

        var commandResult = new
        {
            commandLine,
            commandCharacter,
            messageOutput.Messages,
            messageOutput.ErrorMessages
        };

        return OkJson(commandResult);
    }
}
