namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// A pool of random attributes a synthesised item can draw from (<c>item_rnd_attr_categories</c>).
/// Synthesis materials point at one of these through <c>item_evolving_materials</c>.
/// </summary>
public class ItemRndAttrCategory
{
    public uint Id { get; set; }
    public string Name { get; set; }

    /// <summary>
    /// How far an item in this pool can be pushed. Signed on purpose: the data carries -1 for pools
    /// that grant no grades at all, which a byte would not survive.
    /// </summary>
    public int MaxEvolvingGrade { get; set; }

    public uint ReRollItemSetId { get; set; }

    /// <summary>
    /// Highest material grade this pool accepts; 255 where there is no limit. Signed for the same
    /// reason as <see cref="MaxEvolvingGrade"/> - some rows carry negative values.
    /// </summary>
    public int MaterialGradeLimit { get; set; }

    public uint CurrencyId { get; set; }
    public uint GroupId { get; set; }
}

/// <summary>
/// Min/max range a random attribute rolls in at a given item grade
/// (<c>item_rnd_attr_unit_modifiers</c>).
/// </summary>
public class ItemRndAttrUnitModifierRange
{
    public uint Id { get; set; }
    public uint GroupId { get; set; }
    public byte GradeId { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
}

/// <summary>
/// What one synthesis grade costs and grants inside a pool
/// (<c>item_rnd_attr_category_properties</c>).
/// </summary>
public class ItemRndAttrCategoryProperty
{
    public uint CategoryId { get; set; }
    public byte GradeId { get; set; }

    /// <summary>Experience needed to leave this grade.</summary>
    public uint ReqExp { get; set; }

    /// <summary>Experience a material of this grade is worth when fed to an item.</summary>
    public uint GainExp { get; set; }

    /// <summary>How many random attributes an item at this grade may carry.</summary>
    public byte MaxUnitModifierNum { get; set; }

    /// <summary>Chance, per 10000, that a feed also grants bonus experience.</summary>
    public int BonusExpChance { get; set; }

    /// <summary>Bonus experience range, expressed as a percentage of the base gain.</summary>
    public int BonusExpMin { get; set; }
    public int BonusExpMax { get; set; }

    /// <summary>Experience needed to leave this grade. <c>req_exp</c> is zero on every shipped row.</summary>
    public uint GradeExp { get; set; }

    /// <summary>Cost multiplier formula 64 scales the price with.</summary>
    public int GoldMul { get; set; }

    public byte MaxElementLevel { get; set; }
}

/// <summary>
/// One rung of the element ladder for a synthesis pool
/// (<c>item_rnd_attr_category_elements</c>).
/// </summary>
public class ItemRndAttrCategoryElement
{
    public uint CategoryId { get; set; }
    public byte Level { get; set; }

    /// <summary>Synthesis experience this level costs.</summary>
    public uint ReqExp { get; set; }

    public long Tax { get; set; }
    public int ConsumeLp { get; set; }
}

/// <summary>
/// A weighted bundle of attribute groups a pool can draw from
/// (<c>item_rnd_attr_unit_modifier_group_sets</c>).
/// </summary>
public class ItemRndAttrUnitModifierGroupSet
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public uint CategoryId { get; set; }
    public int Weight { get; set; }

    /// <summary>How many groups are drawn from this set at once.</summary>
    public int PickNum { get; set; }

    public uint InheritPriorityId { get; set; }
}

/// <summary>
/// One attribute a set can roll (<c>item_rnd_attr_unit_modifier_groups</c>): which unit attribute,
/// applied which way, at what weight.
/// </summary>
public class ItemRndAttrUnitModifierGroup
{
    public uint Id { get; set; }
    public int Weight { get; set; }
    public short UnitAttributeId { get; set; }
    public byte UnitModifierTypeId { get; set; }
    public uint GroupSetId { get; set; }

    /// <summary>Marks an attribute the pool always grants rather than rolls for.</summary>
    public bool FixedAttr { get; set; }
}
