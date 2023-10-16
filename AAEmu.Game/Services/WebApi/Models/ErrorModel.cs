namespace AAEmu.Game.Services.WebApi.Models;

public record ErrorModel(string Message, string StackTrace = null);