using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class WearableSlot
{
    public long? Id { get; set; }

    public long? SlotTypeId { get; set; }

    public long? Coverage { get; set; }
}
