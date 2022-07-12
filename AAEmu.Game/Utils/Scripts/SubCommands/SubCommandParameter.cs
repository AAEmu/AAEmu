namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public abstract class SubCommandParameterBase
    {
        protected string GetValueWithoutPrefix(string argumentValue)
        {
            if (Prefix is null)
            {
                return argumentValue;
            }
            return argumentValue.Substring(Prefix.Length + 1);
        }
        public SubCommandParameterBase(string name, bool required)
        {
            Name = name;
            IsRequired = required;
        }
        public string Name { get; protected set; }
        public bool IsRequired { get; protected set; }
        public string Prefix { get; protected set; }
        public abstract ParameterValue Load(string argument);
        public bool MatchPrefix(string argument)
        {
            if (Prefix is null)
                return false;

            return argument.StartsWith(Prefix + "=");
        }
    }
}
