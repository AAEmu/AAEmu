namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One <c>item_elements</c> row: the attack element a holdable declares and the growth of its
/// <c>element_value</c> with the piece's element specialisation level.
/// </summary>
public sealed class ItemElementDesc
{
    public uint Id { get; init; }
    public int DefaultElementValue { get; init; }
    public int AddElementValue { get; init; }
}
