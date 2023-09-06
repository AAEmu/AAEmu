using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemSlaveEquipment
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? SlaveId { get; set; }

    public long? DoodadId { get; set; }

    public long? SlaveEquipPackId { get; set; }

    public long? SlotPackId { get; set; }

    public long? RequireItemId { get; set; }
}
