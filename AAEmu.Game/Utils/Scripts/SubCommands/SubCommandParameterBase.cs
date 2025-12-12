namespace AAEmu.Game.Utils.Scripts.SubCommands;

public abstract class SubCommandParameterBase(string name, string displayName, bool required)
{
    public string Name { get; protected set; } = name;
    public string DisplayName { get; protected set; } = displayName ?? name;
    public bool IsRequired { get; protected set; } = required;
    public string Prefix { get; protected set; }
    public abstract string CallExample { get; }
    /// <summary>
    /// This value will be used if the parameter is optional and no value was provided.
    /// </summary>
    public object DefaultValue { get; set; }
    protected string GetValueWithoutPrefix(string argumentValue)
    {
        if (Prefix is null)
        {
            return argumentValue;
        }
        return argumentValue.Substring(Prefix.Length + 1);
    }
    public bool MatchPrefix(string argument)
    {
        if (Prefix is null)
            return false;

        return argument.StartsWith(Prefix + "=");
    }
    public abstract ParameterResult Load(string argument);

}
