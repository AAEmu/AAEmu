namespace AAEmu.Game.Utils.Scripts.SubCommands;

public class ParameterResult<T>(string name, T value, string invalidMessage = null)
    : ParameterResult(name, value, invalidMessage);
