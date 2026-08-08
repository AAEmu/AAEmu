using System.Numerics;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C.UnitState;

internal sealed class UnitStateWireContext(Unit unit, BaseUnitType baseUnitType, Vector3? placementOverride = null)
{
    public Unit Unit { get; } = unit;
    public BaseUnitType BaseUnitType { get; } = baseUnitType;
    public Character Character { get; } = unit as Character;
    public Npc Npc { get; } = unit as Npc;
    public Vector3? PlacementOverride { get; } = placementOverride;
}
