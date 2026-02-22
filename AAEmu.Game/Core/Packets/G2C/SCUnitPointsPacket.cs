using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitPointsPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    private readonly uint _id;
    private readonly int _preciseHealth;
    private readonly int _preciseMana;

    /// <summary>
    /// Creates packet using the unit's scaled precise health/mana (accounts for NPC bonus scaling).
    /// </summary>
    public SCUnitPointsPacket(Unit unit) : base(SCOffsets.SCUnitPointsPacket, 1)
    {
        _id = unit.ObjId;
        _preciseHealth = unit.GetPreciseHealth();
        _preciseMana = unit.GetPreciseMana();
    }

    /// <summary>
    /// Creates packet from raw HP/MP values (used for Characters and other units without bonus scaling).
    /// </summary>
    public SCUnitPointsPacket(uint id, int health, int mana) : base(SCOffsets.SCUnitPointsPacket, 1)
    {
        _id = id;
        _preciseHealth = health * 100;
        _preciseMana = mana * 100;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_id);
        stream.Write(_preciseHealth);
        stream.Write(_preciseMana);
        return stream;
    }
}
