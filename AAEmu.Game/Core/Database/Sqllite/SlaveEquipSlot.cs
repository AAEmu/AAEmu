using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SlaveEquipSlot
{
    public long? Id { get; set; }

    public long? SlaveId { get; set; }

    public long? AttachPointId { get; set; }

    public long? EquipSlotId { get; set; }

    public long? RequireSlotId { get; set; }
}
