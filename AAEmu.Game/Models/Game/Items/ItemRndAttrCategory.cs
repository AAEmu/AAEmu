namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One row of <c>item_rnd_attr_category_properties</c>: the per-grade tuning of a synthesis
/// ("Item Growth / 합성") category.
/// </summary>
/// <remarks>
/// The 10.0.2.13 client reads this table as
/// <c>SELECT id, bonus_exp_chance, bonus_exp_max, bonus_exp_min, gain_exp, gold_mul, grade_id,
/// grade_exp, item_rnd_attr_category_id, max_element_level, max_unit_modifier_num</c> — note that
/// <c>req_exp</c> is NOT among them. The client's own <c>canEvolve</c> test is
/// <c>gradeExp &gt; item.evolvingExp</c>, so <see cref="GradeExp"/> is the authoritative requirement
/// and the server uses the same field. <c>req_exp</c> (where the shipped data has it at all) is only
/// the running total of <c>grade_exp</c> and is deliberately not loaded.
/// </remarks>
public class ItemRndAttrCategoryProperty
{
    public uint Id { get; set; }
    public uint CategoryId { get; set; }
    public byte GradeId { get; set; }

    /// <summary>EXP this grade must accumulate before the item advances. 0 means it cannot grow further.</summary>
    public int GradeExp { get; set; }

    /// <summary>EXP granted when an item of this category, at this grade, is consumed as a synthesis material.</summary>
    public int GainExp { get; set; }

    /// <summary>Per-mille multiplier feeding the <c>item_evolving_cost</c> formula.</summary>
    public int GoldMul { get; set; }

    public int BonusExpChance { get; set; }
    public int BonusExpMin { get; set; }
    public int BonusExpMax { get; set; }

    /// <summary>How many random attributes an item of this category may carry at this grade.</summary>
    public int MaxUnitModifierNum { get; set; }

    public int MaxElementLevel { get; set; }
}

/// <summary>
/// A synthesis category (<c>item_rnd_attr_categories</c>). Equipment points at one through
/// <c>item_weapons/item_armors/item_accessories.item_rnd_attr_category_id</c>; so does every
/// infusion, through <c>item_evolving_materials</c>.
/// </summary>
public class ItemRndAttrCategory
{
    public uint Id { get; set; }
    public string Name { get; set; }

    /// <summary>Highest item grade this category can be synthesized to. Negative means "never a target".</summary>
    public int MaxEvolvingGrade { get; set; }

    /// <summary>
    /// Group this category belongs to. A material may be fed to a target only when the target group
    /// lists the material's group in <c>item_rnd_attr_category_relations</c>.
    /// </summary>
    public uint GroupId { get; set; }

    public int MaterialGradeLimit { get; set; }
    public uint CurrencyId { get; set; }
    public uint ReRollItemSetId { get; set; }

    public Dictionary<byte, ItemRndAttrCategoryProperty> Properties { get; } = [];

    /// <summary>Attribute pools this category draws its synthesis effects from.</summary>
    public readonly List<ItemRndAttrUnitModifierGroupSet> GroupSets = [];

    public ItemRndAttrCategoryProperty GetProperty(byte grade)
        => Properties.GetValueOrDefault(grade);

    /// <summary>Can an item of this category, currently at <paramref name="grade"/>, still be grown?</summary>
    /// <remarks>
    /// Purely "this grade still has an EXP requirement", which is the client's own test
    /// (<c>grade_exp &gt; evolvingExp</c>; it only validates <c>max_evolving_grade</c> is a real grade
    /// and never compares it to the item). <c>max_evolving_grade</c> must NOT gate this: 338
    /// categories have EXP requirements above it, so the Hiram lines keep taking infusions - and keep
    /// gaining stats - past it, all the way to Eternal.
    /// </remarks>
    public bool CanEvolve(byte grade)
    {
        var property = GetProperty(grade);
        return property is { GradeExp: > 0 };
    }

    /// <summary>True once the bar at this grade is full, i.e. more EXP would be wasted.</summary>
    public bool IsFull(byte grade, int evolvingExp)
    {
        var required = GetProperty(grade)?.GradeExp ?? 0;
        return required <= 0 || evolvingExp >= required;
    }
}

/// <summary>
/// A row of <c>item_evolving_materials</c>: an item usable as a synthesis material, and the category
/// whose per-grade <see cref="ItemRndAttrCategoryProperty.GainExp"/> decides how much it is worth.
/// </summary>
public class ItemEvolvingMaterial
{
    public uint ItemId { get; set; }
    public uint CategoryId { get; set; }
    public bool ShowExp { get; set; }
}

/// <summary>
/// One candidate attribute a synthesis category can grant: which unit attribute, and how much it is
/// worth at each item grade.
/// </summary>
public class ItemRndAttrUnitModifierGroup
{
    public uint Id { get; set; }
    public uint GroupSetId { get; set; }
    public uint UnitAttributeId { get; set; }
    public uint UnitModifierTypeId { get; set; }

    /// <summary>Relative odds of being drawn from its set.</summary>
    public int Weight { get; set; }

    /// <summary>Always granted rather than drawn.</summary>
    public bool FixedAttr { get; set; }

    /// <summary>Grade -> the value this attribute is worth there.</summary>
    public Dictionary<byte, int> ValueByGrade { get; } = [];

    public int GetValue(byte grade) => ValueByGrade.GetValueOrDefault(grade);
}

/// <summary>
/// A pool of candidate attributes. A category draws <see cref="PickNum"/> of them, which is how an
/// item ends up with e.g. one main stat and two bonus lines.
/// </summary>
public class ItemRndAttrUnitModifierGroupSet
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public uint CategoryId { get; set; }
    public int Weight { get; set; }
    public int PickNum { get; set; }
    public uint InheritPriorityId { get; set; }

    public readonly List<ItemRndAttrUnitModifierGroup> Groups = [];
}
