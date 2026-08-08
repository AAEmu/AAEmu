using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C.UnitState;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Client UnitState envelope. The shared unit body and each nested wire block live under
/// <see cref="UnitStateWireSerializer"/> so SCUnitState, WZUnitState, and WZNpcState cannot drift.
/// Client packets use world placement; Zone packets may provide an explicit local placement.
/// </summary>
public sealed class SCUnitStatePacket : GamePacket
{
    private readonly Unit _unit;
    private readonly BaseUnitType _baseUnitType;

    public SCUnitStatePacket(Unit unit) : base(SCOffsets.SCUnitStatePacket, 1)
    {
        _unit = unit;
        _baseUnitType = UnitStateWireSerializer.GetBaseUnitType(unit);
    }

    public void WriteWzUnitStateAndBuffs(PacketStream stream, Vector3? placementOverride = null)
    {
        UnitStateWireSerializer.Write(stream, _unit, _baseUnitType, placementOverride);
        UnitStateBuffSerializer.Write(stream, _unit);
    }

    /// <summary>
    /// Writes the Zone UnitState body, its buffs, and action state.
    /// </summary>
    public void WriteWzBody(PacketStream stream, Vector3? placementOverride = null)
    {
        WriteWzUnitStateAndBuffs(stream, placementOverride);
        UnitStateActionSerializer.Write(stream, _unit.UnitStateAction);
    }

    public override PacketStream Write(PacketStream stream)
    {
        var body = new PacketStream();
        UnitStateWireSerializer.Write(body, _unit, _baseUnitType);
        if (_baseUnitType == BaseUnitType.Npc)
        {
            body.Write((byte)0);
            body.Write((byte)0);
            body.Write((byte)0);
        }
        else
            UnitStateBuffSerializer.Write(body, _unit);

        stream.Write(body, false);
        return stream;
    }

    public override string Verbose() => " - " + _baseUnitType + " - " + _unit?.DebugName();
}
