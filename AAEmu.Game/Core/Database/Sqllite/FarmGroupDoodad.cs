using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class FarmGroupDoodad
{
    public long? Id { get; set; }

    public long? FarmGroupId { get; set; }

    public long? DoodadId { get; set; }

    public long? ItemId { get; set; }
}
