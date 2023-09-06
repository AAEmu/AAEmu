using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncShear
{
    public long? Id { get; set; }

    public long? ShearTypeId { get; set; }

    public long? ShearTerm { get; set; }
}
