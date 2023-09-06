using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemLookConvertWearable
{
    public long? Id { get; set; }

    public long? ItemLookConvertId { get; set; }

    public long? WearableSlotId { get; set; }
}
