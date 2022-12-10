using System.Collections.Generic;
using System.Linq;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class StringSubCommandParameter : SubCommandParameterBase
    {
        private List<string> _values = new List<string>();
        public StringSubCommandParameter(string name, string displayName, bool isRequired, params string[] validValues) : base(name, displayName, isRequired)
        {
            _values.AddRange(validValues.Select(s => s.ToLower()));
        }
        public StringSubCommandParameter(string name, string displayName, bool isRequired, string prefix) : base(name, displayName, isRequired)
        {
            Prefix = prefix;
        }

        public override string CallExample => 
            _values.Count == 0 
                ? DisplayName
                : $"{string.Join("||", _values)}";
        
        public override ParameterResult Load(string argumentValue)
        {
            if (_values.Count == 0)
            {
                return new ParameterResult<string>(Name, GetValueWithoutPrefix(argumentValue));
            }

            if (!_values.Contains(argumentValue.ToLower()))
            {
                return new ParameterResult<string>(Name, null, $"Parameter [{DisplayName}] only accepts: {string.Join("||", _values)}");
            }
            return new ParameterResult<string>(Name, argumentValue);
        }
    }
}
