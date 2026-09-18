namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// The three reinforcement attributes an equip slot ladder can belong to. Each slot type has exactly
/// one, and the totals per attribute gate the set and bundle effects.
/// </summary>
public enum EquipSlotReinforceAttribute : byte
{
    Offence = 1,
    Defence = 2,
    Support = 3
}

/// <summary>
/// One step of a slot's reinforcement ladder: what the next level costs and what reaching it gives.
/// </summary>
public class EquipSlotReinforceStep
{
    public uint Id { get; set; }
    public byte SlotTypeId { get; set; }
    public byte Level { get; set; }
    public int NeedExp { get; set; }
    public EquipSlotReinforceAttribute Attribute { get; set; }

    /// <summary>Item level the equipped piece gains at this level.</summary>
    public float GainItemLevel { get; set; }

    /// <summary>Item the level-up itself consumes, and how many.</summary>
    public uint LevelUpItemId { get; set; }

    public int LevelUpItemCount { get; set; }
}

/// <summary>
/// One way to feed a slot: the item set it draws from, the exp it grants, and the level it unlocks at.
/// A slot usually has several of these per level, trading cost against experience.
/// </summary>
public class EquipSlotReinforceMaterial
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte RequireLevel { get; set; }
    public int GainExp { get; set; }

    /// <summary>
    /// Currency the feeding charges (<see cref="ContentCurrencyType"/>; 0 is gold) and how much of it.
    /// <see cref="CurrencyValue"/> 0 means the feeding is free.
    /// </summary>
    public uint CurrencyId { get; set; }

    public int CurrencyValue { get; set; }

    /// <summary>Item set the material is drawn from.</summary>
    public uint NeedMaterialItemSetId { get; set; }

    public byte SlotTypeId { get; set; }
}

/// <summary>A level effect a slot becomes eligible for, with the modifiers it applies.</summary>
public class EquipSlotReinforceLevelEffect
{
    public uint Id { get; set; }
    public byte SlotTypeId { get; set; }
    public byte TriggerLevel { get; set; }
    public List<EquipSlotReinforceUnitModifier> Modifiers { get; } = [];
}

/// <summary>One unit modifier a level effect applies once it is the chosen effect of its slot.</summary>
public class EquipSlotReinforceUnitModifier
{
    public uint Id { get; set; }
    public uint LevelEffectId { get; set; }
    public uint UnitAttributeId { get; set; }
    public uint UnitModifierTypeId { get; set; }
    public int Value { get; set; }
    public int Weight { get; set; }
}

/// <summary>A bonus that unlocks at a total level across every slot of one attribute.</summary>
public class EquipSlotReinforceSetEffect
{
    public uint Id { get; set; }
    public EquipSlotReinforceAttribute Attribute { get; set; }
    public byte RequireReinforceLevel { get; set; }
    public byte Level { get; set; }
    public string Desc { get; set; } = string.Empty;
}

/// <summary>A bonus that unlocks when all three attribute totals reach their own thresholds.</summary>
public class EquipSlotReinforceBundleEffect
{
    public uint Id { get; set; }
    public byte BundleEffectLevel { get; set; }
    public byte RequireOffenseLevel { get; set; }
    public byte RequireDefenseLevel { get; set; }
    public byte RequireSupportLevel { get; set; }
    public string Desc { get; set; } = string.Empty;
}

/// <summary>
/// A character's progress on one equip slot: the level it reached and the exp banked towards the next
/// one. The ladder it climbs belongs to the slot, not to the item sitting in it, which is why this
/// survives swapping gear.
/// </summary>
public class EquipSlotReinforceState
{
    public byte SlotTypeId { get; set; }
    public sbyte Level { get; set; }
    public int Exp { get; set; }

    public EquipSlotReinforceState Clone()
    {
        return new EquipSlotReinforceState
        {
            SlotTypeId = SlotTypeId,
            Level = Level,
            Exp = Exp
        };
    }
}

/// <summary>
/// One artifact effect a slot has obtained: the tier it came from and the single modifier row the roll gave
/// it. A tier offers several rows and the player gets one of them, so (slot, tier) identifies the effect and
/// the row is what it is worth — that is why this stores a row id rather than the stats themselves.
/// </summary>
public class EquipSlotReinforceEffect
{
    public byte SlotTypeId { get; set; }

    /// <summary>The tier this effect came from, i.e. the <c>equip_slot_reinforce_level_effects</c> row.</summary>
    public uint LevelEffectId { get; set; }

    /// <summary>The <c>equip_slot_reinforce_unit_modifiers</c> row the roll picked out of that tier.</summary>
    public uint UnitModifierId { get; set; }

    /// <summary>
    /// Whether the effect is switched on for the character. It is on from the moment it is rolled, and the
    /// window's radio is what switches one off again ("None") or back on.
    /// </summary>
    public bool Applied { get; set; } = true;

    public EquipSlotReinforceEffect Clone()
    {
        return new EquipSlotReinforceEffect
        {
            SlotTypeId = SlotTypeId,
            LevelEffectId = LevelEffectId,
            UnitModifierId = UnitModifierId,
            Applied = Applied
        };
    }
}
