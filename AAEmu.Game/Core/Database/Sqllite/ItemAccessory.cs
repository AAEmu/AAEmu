using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemAccessory
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? SlotTypeId { get; set; }

    public long? TypeId { get; set; }

    public long? ModSetId { get; set; }

    public long? EisetId { get; set; }

    public byte[] OrUnitReqs { get; set; }

    public byte[] Repairable { get; set; }

    public long? DurabilityMultiplier { get; set; }

    public long? RechargeBuffId { get; set; }

    public long? ChargeLifetime { get; set; }

    public long? ChargeCount { get; set; }
}
