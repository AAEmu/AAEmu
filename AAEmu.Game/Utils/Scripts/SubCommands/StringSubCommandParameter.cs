﻿using System.Collections.Generic;
using System.Linq;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class StringSubCommandParameter : SubCommandParameterBase
    {
        private List<string> _values = new List<string>();
        public StringSubCommandParameter(string name, bool isRequired, params string[] validValues) : base(name, isRequired)
        {
            _values.AddRange(validValues.Select(s => s.ToLower()));
        }
        public StringSubCommandParameter(string name, bool isRequired, string prefix) : base(name, isRequired)
        {
            Prefix = prefix;
        }
        public override ParameterValue Load(string argumentValue)
        {
            if (_values.Count == 0)
            {
                return new ParameterValue<string>(Name, GetValueWithoutPrefix(argumentValue));
            }

            if (!_values.Contains(argumentValue.ToLower()))
            {
                return new ParameterValue<string>(Name, null, $"Parameter {Name} only accepts those values: {string.Join("||", _values)}");
            }
            return new ParameterValue<string>(Name, argumentValue);
        }
    }
}
