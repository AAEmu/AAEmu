namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public abstract class SubCommandParameterBase
    {
        public SubCommandParameterBase(string name, string displayName, bool required)
        {
            Name = name;
            IsRequired = required;
            DisplayName = displayName ?? name;
        }
        public string Name { get; protected set; }
        public string DisplayName { get; protected set; }
        public bool IsRequired { get; protected set; }
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
}
