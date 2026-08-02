using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCConflictZoneStatePacket(ushort zoneId, ZoneConflictType hpws, DateTime endTime, DateTime lockTime = default)
    : GamePacket(SCOffsets.SCConflictZoneStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // type(zoneId u16) | hpws(u8) | end(i64) | lock(i64).
        stream.Write(zoneId);       // "type"
        stream.Write((byte)hpws);   // "hpws"
        stream.Write(endTime);      // "end"
        stream.Write(lockTime);     // "lock"
        return stream;
    }
}
