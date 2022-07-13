﻿namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public abstract class SubCommandParameterBase
    {
        public SubCommandParameterBase(string name, bool required)
        {
            Name = name;
            IsRequired = required;
        }
        public string Name { get; protected set; }
        public bool IsRequired { get; protected set; }
        public string Prefix { get; protected set; }

        /// <summary>
        /// This value will be used if the parameter is optional and no value was provided.
        /// </summary>
        public object DefaultValue { get; init; }
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
