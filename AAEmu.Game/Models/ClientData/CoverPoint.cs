using System.Numerics;

namespace AAEmu.Game.Models.ClientData;

/// <summary>
/// AI cover point from cover.ctc — position where NPCs can take cover in combat.
/// </summary>
public class CoverPoint
{
    public Vector3 Pos { get; set; }
    public Vector3 Dir { get; set; }
}
