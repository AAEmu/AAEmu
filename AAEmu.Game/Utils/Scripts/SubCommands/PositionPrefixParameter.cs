namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class PositionPrefixParameter : SubCommandParameterBase
    {
        
        public PositionPrefixParameter(string name, string prefix) : base(name, false)
        {
            Prefix = prefix;
        }


        public override ParameterValue Load(string argument)
        {
            if (!MatchPrefix(argument))
            {
                return new ParameterValue<float>(Name, default, $"Invalid parameters format, usage: {Prefix}=<value>");
            }

            if (float.TryParse(argument.Split('=')[1], out var argumentValue))
            {
                return new ParameterValue<float>(Name, argumentValue);
            }
            else
            {
                return new ParameterValue<float>(Name, default, $"Invalid value [{argument}] for parameter prefix {Prefix}");
            }
        }
    }
}
