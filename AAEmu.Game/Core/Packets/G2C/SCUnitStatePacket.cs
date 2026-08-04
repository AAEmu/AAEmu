using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C.UnitState;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Client UnitState envelope. The shared unit body and each nested wire block live under
/// <see cref="UnitStateWireSerializer"/> so SCUnitState, WZUnitState, and WZNpcState cannot drift.
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

    public void WriteWzUnitStateAndBuffs(PacketStream stream)
    {
        UnitStateWireSerializer.Write(stream, _unit, _baseUnitType);
        UnitStateBuffSerializer.Write(stream, _unit);
    }

    /// <summary>
    /// WZUnitState 0x007 body: UnitState + buffs + action state. The action serializer owns
    /// </summary>
    public void WriteWzBody(PacketStream stream)
    {
        WriteWzUnitStateAndBuffs(stream);
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
