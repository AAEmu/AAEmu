using System;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class NumericSubCommandParameter<T> : SubCommandParameterBase
    {
        private readonly T _minValue;
        private readonly T _maxValue;
        public NumericSubCommandParameter(string name, bool required, T minValue, T maxValue) : base(name, required)
        {
            _minValue = minValue;
            _maxValue = maxValue;
        }
        public NumericSubCommandParameter(string name, bool required) : base(name, required)
        {
            switch (typeof(T).Name)
            {
                case "Int32":
                    _minValue = (T)Convert.ChangeType(int.MinValue, typeof(T));
                    _maxValue = (T)Convert.ChangeType(int.MaxValue, typeof(T));
                    break;
                case "Int64":
                    _minValue = (T)Convert.ChangeType(long.MinValue, typeof(T));
                    _maxValue = (T)Convert.ChangeType(long.MaxValue, typeof(T));
                    break;
                case "Single":
                    _minValue = (T)Convert.ChangeType(float.MinValue, typeof(T));
                    _maxValue = (T)Convert.ChangeType(float.MaxValue, typeof(T));
                    break;
                case "UInt32":
                    _minValue = (T)Convert.ChangeType(uint.MinValue, typeof(T));
                    _maxValue = (T)Convert.ChangeType(uint.MaxValue, typeof(T));
                    break;
                case "Byte":
                    _minValue = (T)Convert.ChangeType(byte.MinValue, typeof(T));
                    _maxValue = (T)Convert.ChangeType(byte.MaxValue, typeof(T));
                    break;
                default:
                    throw new Exception($"Unsupported numeric type {typeof(T).Name}");
            }
        }

        public override ParameterValue Load(string textValue)
        {
            T result;
            bool isValidNumber = false;
            bool isValidRange = false;
            string invalidMessage = null;

            switch (typeof(T).Name)
            {
                case "Int32":
                    {
                        (isValidNumber, result) = (int.TryParse(textValue, out var intValue), (T)Convert.ChangeType(intValue, typeof(T)));
                        if (isValidNumber)
                        {
                            isValidRange = intValue >= Convert.ToInt32(_minValue) && intValue <= Convert.ToInt32(_maxValue);
                        }
                        break;
                    }
                case "Int64":
                    {
                        (isValidNumber, result) = (long.TryParse(textValue, out var longValue), (T)Convert.ChangeType(longValue, typeof(T)));
                        if (isValidNumber)
                        {
                            isValidRange = longValue >= Convert.ToInt64(_minValue) && longValue <= Convert.ToInt64(_maxValue);
                        }
                        break;
                    }
                case "Single":
                    {
                        (isValidNumber, result) = (float.TryParse(textValue, out var singleValue), (T)Convert.ChangeType(singleValue, typeof(T)));
                        if (isValidNumber)
                        {
                            isValidRange = singleValue >= Convert.ToSingle(_minValue) && singleValue <= Convert.ToSingle(_maxValue);
                        }
                        break;
                    }
                case "UInt32":
                    {
                        (isValidNumber, result) = (uint.TryParse(textValue, out var uintValue), (T)Convert.ChangeType(uintValue, typeof(T)));
                        if (isValidNumber)
                        {
                            isValidRange = uintValue >= Convert.ToUInt32(_minValue) && uintValue <= Convert.ToUInt32(_maxValue);
                        }
                        break;
                    }
                case "Byte":
                    {

                        (isValidNumber, result) = (byte.TryParse(textValue, out var byteValue), (T)Convert.ChangeType(byteValue, typeof(T)));
                        if (isValidNumber)
                        {
                            isValidRange = byteValue >= Convert.ToByte(_minValue) && byteValue <= Convert.ToByte(_maxValue);
                        }
                        break;
                    }
                default:
                    {
                        result = default(T);
                        invalidMessage = $"Unsupported numeric type {typeof(T).Name}";

                        return new ParameterValue<T>(Name, result, invalidMessage);
                    }
            }

            if (!isValidNumber)
            {
                invalidMessage = $"Invalid numeric value for parameter: {Name}";
            }
            else if (!isValidRange)
            {
                invalidMessage = $"Invalid numeric range ({_minValue}-{_maxValue}) for parameter: {Name}";
            }

            return new ParameterValue<T>(Name, result, invalidMessage);
        }
    }
}
