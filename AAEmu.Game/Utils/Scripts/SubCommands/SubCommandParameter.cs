using System;
using System.Collections.Generic;
using System.Linq;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public abstract class SubCommandParameterDefinition<T> where T : class
    {
        public string Name { get; protected set; }
        public Type Type { get; protected set; }
        public bool IsOptional { get; protected set; }

        public abstract (bool, T) IsValid(string value);
    }

    public class StringSubCommandParameter : SubCommandParameterDefinition<string>
    {
        private List<string> _values = new List<string>();
        public StringSubCommandParameter(bool optional, params string[] validValues)
        {
            IsOptional = optional;
            _values.AddRange(validValues.Select(s => s.ToLower()));
            Type = typeof(string);
        }

        public override (bool, string) IsValid(string value)
        {
            return (_values.Contains(value.ToLower()), value.ToLower());
        }
    }

    public class NumericSubCommandParameter<T> : SubCommandParameterDefinition<T> where T : class
    {
        private readonly T _minValue;
        private readonly T _maxValue;
        public NumericSubCommandParameter(bool optional, T minValue, T maxValue)
        {
            IsOptional = optional;
            _minValue = minValue;
            _maxValue = maxValue;
            Type = typeof(T);
        }
        public NumericSubCommandParameter()
        {
            switch (typeof(T).Name)
            {
                case "Int32":
                    _minValue = int.MinValue as T;
                    _maxValue = int.MaxValue as T;
                    break;
                case "Int64":
                    _minValue = long.MinValue as T;
                    _maxValue = long.MaxValue as T;
                    break;
                case "Single":
                    _minValue = float.MinValue as T;
                    _maxValue = float.MaxValue as T;
                    break;
                case "UInt32":
                    _minValue = uint.MinValue as T;
                    _maxValue = uint.MaxValue as T;
                    break;
                case "Byte":
                    _minValue = byte.MinValue as T;
                    _maxValue = byte.MaxValue as T;
                    break;
                default:
                    throw new Exception($"Unsupported numeric type {typeof(T).Name}");
            }
        }

        public override (bool, T) IsValid(string textValue)
        {
            switch (typeof(T).Name)
            {
                case "Int32":
                    return (int.TryParse(textValue, out var intValue), intValue as T);
                case "Int64":
                    return (long.TryParse(textValue, out var longValue), longValue as T);
                case "Single":
                    return (float.TryParse(textValue, out var singleValue), singleValue as T);
                case "UInt32":
                    return (uint.TryParse(textValue, out var uintValue), uintValue as T);
                case "Byte":
                    return (byte.TryParse(textValue, out var byteValue), byteValue as T);
                default:
                    throw new Exception($"Unsupported numeric type {typeof(T).Name}");
            }
        }
    }
}
