namespace AAEmu.Game.Utils.Scripts.SubCommands;

public class ParameterResult(string name, object value, string invalidMessage = null)
{
    public bool IsValid => string.IsNullOrEmpty(InvalidMessage);
    public string Name { get; } = name;
    public ParameterValue Value { get; } = new(value);
    public string InvalidMessage { get; } = invalidMessage;
}
