using System;
using System.Globalization;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class NumericSubCommandParameter<T> : SubCommandParameterBase
    {
        private readonly T _minValue;
        private readonly T _maxValue;

        public override string CallExample => DisplayName;

        public NumericSubCommandParameter(string name, string displayName, bool required, T minValue, T maxValue) 
            : this(name, displayName, required, null, minValue, maxValue)
        {
        }

        public NumericSubCommandParameter(string name, string displayName, bool required, string prefix, T minValue, T maxValue) 
            : base(name, displayName, required)
        {
            EnsureValidRanges(minValue, maxValue);
            _minValue = minValue;
            _maxValue = maxValue;
            Prefix = prefix;
        }
        public NumericSubCommandParameter(string name, string displayName, bool required, string prefix) 
            : this(name, displayName, required)
        {
            Prefix = prefix;
        }
        
        public NumericSubCommandParameter(string name, string displayName, bool required) 
            : base(name, displayName, required)
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
                case "UInt64":
                    _minValue = (T)Convert.ChangeType(ulong.MinValue, typeof(T));
                    _maxValue = (T)Convert.ChangeType(ulong.MaxValue, typeof(T));
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
                case "Float":
                    _minValue = (T)Convert.ChangeType(float.MinValue, typeof(T));
                    _maxValue = (T)Convert.ChangeType(float.MaxValue, typeof(T));
                    break;
                case "Double":
                    _minValue = (T)Convert.ChangeType(double.MinValue, typeof(T));
                    _maxValue = (T)Convert.ChangeType(double.MaxValue, typeof(T));
                    break;
            }
        }

        private void EnsureValidRanges(T minValue, T maxValue)
        {
            bool isValidRange = false;
            
            switch (typeof(T).Name)
            {
                case "Int32":
                    isValidRange = Convert.ToInt32(minValue) <= Convert.ToInt32(maxValue);
                    break;
                case "Int64":
                    isValidRange = Convert.ToInt64(minValue) <= Convert.ToInt64(maxValue);
                    break;
                case "UInt64":
                    isValidRange = Convert.ToUInt64(minValue) <= Convert.ToUInt64(maxValue);
                    break;
                case "Single":
                    isValidRange = Convert.ToSingle(minValue) <= Convert.ToSingle(maxValue);
                    break;
                case "UInt32":
                    isValidRange = Convert.ToUInt32(minValue) <= Convert.ToUInt32(maxValue);
                    break;
                case "Byte":
                    isValidRange = Convert.ToByte(minValue) <= Convert.ToByte(maxValue);
                    break;
                case "Float":
                    isValidRange = Convert.ToSingle(minValue) <= Convert.ToSingle(maxValue);
                    break;
                case "Double":
                    isValidRange = Convert.ToDouble(minValue) <= Convert.ToDouble(maxValue);
                    break;
            }

            if (!isValidRange)
            {
                throw new ArgumentOutOfRangeException(nameof(_minValue), $"Parameter [{DisplayName}] minimum value {minValue} must be less than or equal to maximum value");
            }
        }
        public override ParameterResult Load(string argumentValue)
        {
            T result;
            var textValue = GetValueWithoutPrefix(argumentValue);
            bool isValidNumber;
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
                        (isValidNumber, result) = (float.TryParse(textValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var singleValue), (T)Convert.ChangeType(singleValue, typeof(T)));
                        if (isValidNumber)
                        {
                            isValidRange = singleValue >= Convert.ToSingle(_minValue) && singleValue <= Convert.ToSingle(_maxValue);
                        }
                        break;
                    }
                case "Double":
                    {
                        (isValidNumber, result) = (double.TryParse(textValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var doubleValue), (T)Convert.ChangeType(doubleValue, typeof(T)));
                        if (isValidNumber)
                        {
                            isValidRange = doubleValue >= Convert.ToDouble(_minValue) && doubleValue <= Convert.ToDouble(_maxValue);
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
                case "UInt64":
                    {
                        (isValidNumber, result) = (ulong.TryParse(textValue, out var ulongValue), (T)Convert.ChangeType(ulongValue, typeof(T)));
                        if (isValidNumber)
                        {
                            isValidRange = ulongValue >= Convert.ToUInt64(_minValue) && ulongValue <= Convert.ToUInt64(_maxValue);
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
                        invalidMessage = $"Unsupported numeric type {typeof(T).Name} for parameter: {DisplayName}";

                        return new ParameterResult<T>(Name, result, invalidMessage);
                    }
            }

            if (!isValidNumber)
            {
                invalidMessage = $"Invalid numeric value for parameter: {DisplayName}";
            }
            else if (!isValidRange)
            {
                invalidMessage = $"The number {result} should be between {_minValue} and {_maxValue} for parameter: {DisplayName}";
            }

            return new ParameterResult<T>(Name, result, invalidMessage);
        }
    }
}
