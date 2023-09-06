using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class GameScheduleDoodad
{
    public long? Id { get; set; }

    public long? GameScheduleId { get; set; }

    public long? DoodadId { get; set; }
}
