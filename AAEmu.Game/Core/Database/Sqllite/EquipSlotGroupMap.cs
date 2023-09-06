using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class EquipSlotGroupMap
{
    public long? Id { get; set; }

    public long? EquipSlotGroupId { get; set; }

    public long? EquipSlotTypeId { get; set; }
}
