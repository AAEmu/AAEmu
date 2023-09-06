using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemWeapon
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? AssetId { get; set; }

    public byte[] BaseEnchantable { get; set; }

    public long? HoldableId { get; set; }

    public long? ModSetId { get; set; }

    public long? EisetId { get; set; }

    public byte[] OrUnitReqs { get; set; }

    public byte[] Repairable { get; set; }

    public long? DurabilityMultiplier { get; set; }

    public byte[] BaseEquipment { get; set; }

    public double? DrawnScale { get; set; }

    public double? WornScale { get; set; }

    public long? RechargeBuffId { get; set; }

    public long? ChargeLifetime { get; set; }

    public long? ChargeCount { get; set; }

    public byte[] UseAsStat { get; set; }

    public long? SkinKindId { get; set; }

    public long? FixedVisualEffectId { get; set; }
}
