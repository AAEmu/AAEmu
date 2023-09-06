using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemEnchantingGem
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? EquipSlotGroupId { get; set; }

    public long? GemVisualEffectId { get; set; }

    public long? EquipLevel { get; set; }

    public long? ItemGradeId { get; set; }
}
