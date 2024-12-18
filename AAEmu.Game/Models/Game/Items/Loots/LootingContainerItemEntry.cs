using System.Collections.Generic;
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
    public LootingContainer Owner { get; set; }
    /// <summary>
    /// Item index within the LootingContainer
    /// </summary>
    public ushort ItemIndex { get; set; }
    /// <summary>
    /// List of the current roll results of all eligible player (PlayerId, RollResult) 
    /// </summary>
    public Dictionary<uint, sbyte> PlayerRolls { get; set; } = new();
    /// <summary>
    /// PlayerId of the highest roller (or the person that claimed this loot entry)
    /// </summary>
    public uint HighestRoller { get; set; }
    public Item Item { get; set; }
}
