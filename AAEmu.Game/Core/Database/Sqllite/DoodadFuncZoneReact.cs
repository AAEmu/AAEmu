using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncZoneReact
{
    public long? Id { get; set; }

    public long? ZoneGroupId { get; set; }

    public long? NextPhase { get; set; }
}
