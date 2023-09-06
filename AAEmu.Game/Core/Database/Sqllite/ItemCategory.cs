using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemCategory
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? ProcessedStateId { get; set; }

    public long? UsageId { get; set; }

    public long? Impl1Id { get; set; }

    public long? Impl2Id { get; set; }

    public long? PickupSoundId { get; set; }

    public long? CategoryOrder { get; set; }

    public long? ItemGroupId { get; set; }

    public long? UseOrEquipmentSoundId { get; set; }

    public byte[] Secure { get; set; }
}
