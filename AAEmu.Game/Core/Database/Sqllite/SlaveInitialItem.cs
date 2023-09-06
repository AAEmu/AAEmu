using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SlaveInitialItem
{
    public long? Id { get; set; }

    public long? SlaveInitialItemPackId { get; set; }

    public long? EquipSlotId { get; set; }

    public long? ItemId { get; set; }
}
