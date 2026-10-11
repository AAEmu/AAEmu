namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One <c>armor_element_resists</c> row: how one armor type answers one attack element.
/// </summary>
public sealed class ArmorElementResistDesc
{
    public uint Id { get; init; }
    public uint ArmorTypeId { get; init; }
    public uint ItemElementId { get; init; }
    public int DefaultElementResistValue { get; init; }
    public int AddElementResistValue { get; init; }

    /// <summary>
    /// Shipped as 0..1000. Formula 65's <c>element_effect_ratio</c> is this value over 1000.
    /// </summary>
    public int EffectRatio { get; init; }
}
