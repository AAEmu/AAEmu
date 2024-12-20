using System.Collections.Generic;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.Game.Models.Game.Items.Loots;

/// <summary>
/// A single item entry of a looting container
/// </summary>
public class LootingContainerItemEntry
{
    /// <summary>
    /// LootingContainer owning this entry
    /// </summary>
    public LootingContainer Owner { get; init; }
    /// <summary>
    /// Item index within the LootingContainer
    /// </summary>
    public ushort ItemIndex { get; init; }
    /// <summary>
    /// List of the current roll results of all eligible player (PlayerId, RollResult), roll results: 0=not rolled, -1=pass 
    /// </summary>
    public Dictionary<Character, sbyte> PlayerRolls { get; } = [];
    /// <summary>
    /// PlayerId of the highest roller (or the person that claimed this loot entry)
    /// </summary>
    public uint HighestRoller { get; set; }
    /// <summary>
    /// The generated Item
    /// </summary>
    public Item Item { get; init; }
}
