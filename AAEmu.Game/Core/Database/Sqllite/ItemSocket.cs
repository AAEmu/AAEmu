using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemSocket
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? EquipSlotGroupId { get; set; }
}
