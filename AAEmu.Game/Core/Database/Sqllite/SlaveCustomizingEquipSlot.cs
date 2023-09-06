using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SlaveCustomizingEquipSlot
{
    public long? Id { get; set; }

    public long? SlaveCustomizingId { get; set; }

    public long? EquipSlotId { get; set; }

    public string EquipSlotName { get; set; }
}
