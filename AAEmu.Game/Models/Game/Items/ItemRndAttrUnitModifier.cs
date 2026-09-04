namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One rolled random attribute on a synthesised ("evolving") item.
/// </summary>
/// <remarks>
/// This is the element the client's shared modifier serializer  reads:
/// <c>attr</c> as i16, <c>type</c> as i8 and <c>value</c> as u32, in that order. Both the synthesis
/// result and the effect-swap result embed a run of these.
/// </remarks>
public class ItemRndAttrUnitModifier
{
    /// <summary>Unit attribute the modifier applies to (<c>unit_attribute_id</c>).</summary>
    public short Attribute { get; set; }

    /// <summary>Modifier kind (flat / percent), mirroring <c>unit_modifier_type_id</c>.</summary>
    public byte ModifierType { get; set; }

    /// <summary>Rolled amount, between the group's min and max for the item's grade.</summary>
    /// <remarks>
    /// Signed. The reducing effects are stored negative - a piece listing "Received Melee Damage
    /// -2.5%" carries -25 - so holding this unsigned flattened every one of them to nothing.
    /// </remarks>
    public int Value { get; set; }
}
