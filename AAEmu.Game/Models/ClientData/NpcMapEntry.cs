using System.Numerics;

namespace AAEmu.Game.Models.ClientData;

/// <summary>
/// An NPC position entry from map_data/npc_map zone files.
/// Each entry contains an NPC template ID and its world position.
/// </summary>
public class NpcMapEntry
{
    public uint NpcId { get; set; }
    public Vector3 Pos { get; set; }
}
