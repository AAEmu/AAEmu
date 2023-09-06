using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class MateEquipPackItem
{
    public long? Id { get; set; }

    public long? MateEquipPackId { get; set; }

    public long? ItemId { get; set; }
}
