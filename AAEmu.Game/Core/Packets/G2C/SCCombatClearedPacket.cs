using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using NLog;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCombatClearedPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    private readonly uint _objId;

    public SCCombatClearedPacket(uint objId) : base(SCOffsets.SCCombatClearedPacket, 1)
    {
        _objId = objId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_objId);
        return stream;
    }
}
