using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class DoodadFuncLootItem
{
    public long? Id { get; set; }

    public long? WiId { get; set; }

    public long? ItemId { get; set; }

    public long? CountMin { get; set; }

    public long? CountMax { get; set; }

    public long? Percent { get; set; }

    public long? RemainTime { get; set; }

    public long? GroupId { get; set; }
}
