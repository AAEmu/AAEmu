using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class AllowToEquipSlot
{
    public long? Id { get; set; }

    public long? SlaveEquipmentEquipSlotPackId { get; set; }

    public long? EquipSlotId { get; set; }
}
