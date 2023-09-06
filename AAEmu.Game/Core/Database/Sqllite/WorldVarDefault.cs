using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class WorldVarDefault
{
    public long? Id { get; set; }

    public long? KindId { get; set; }

    public string VariableName { get; set; }

    public long? DefaultValue { get; set; }
}
