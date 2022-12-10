using System;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class ParameterResult
    {
        public ParameterResult(string name, object value, string invalidMessage = null)
        {
            Name = name;
            Value = new ParameterValue(value);
            InvalidMessage = invalidMessage;
        }
        public bool IsValid => string.IsNullOrEmpty(InvalidMessage);
        public string Name { get; }
        public ParameterValue Value { get; }
        public string InvalidMessage { get; }
    }
}
