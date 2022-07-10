using System;
using System.Collections.Generic;
using System.Linq;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class SubCommandParameterConfig
    {
        List<SubCommandParameterDefinition<object>> _parameters;

        public SubCommandParameterConfig AddParameter(SubCommandParameterDefinition<object> parameter)
        {
            if (!parameter.IsOptional && _parameters.Last().IsOptional)
            {
                throw new Exception("Cannot add required parameter after optional parameter");
            }

            _parameters.Add(parameter);
            return this;
        }
    }
}
