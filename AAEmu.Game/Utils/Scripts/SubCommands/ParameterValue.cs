using System;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class ParameterValue
    {
        public ParameterValue(string name, object value, string invalidMessage = null)
        {
            Name = name;
            Value = value;
            InvalidMessage = invalidMessage;
        }
        public bool IsValid => string.IsNullOrEmpty(InvalidMessage);
        public string Name { get; }
        public object Value { get; }
        public string InvalidMessage { get; }

        public T GetValue<T>()
        {
            if (Value is T)
                return (T)Value;
            throw new ArgumentException($"Invalid value type for parameter {Name}");
        }
    }
}
