using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitPointsPacket(uint id, int health, int mana) : GamePacket(SCOffsets.SCUnitPointsPacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    // 10.0.2.13: precise hp/mp are i64 (same as SCUnitState). i32 truncates the body and
    // the client logs "not enough buffer for preciseMana" → sc sequence desync → disconnect.
    private readonly long _preciseHealth = (long)health * 100;
    private readonly long _preciseMana = (long)mana * 100;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(id);
        stream.Write(_preciseHealth);
        stream.Write(_preciseMana);
        return stream;
    }
}
