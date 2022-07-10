using System;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    internal class SubCommandParameterDefinition
    {
        public string Name { get; set; }
        public Type Type { get; set; }
    }

    internal class RequiredStringSubCommandParameter : SubCommandParameterDefinition
    {
        public RequiredStringSubCommandParameter()
        {
            Type = typeof(string);
        }
    }
}
