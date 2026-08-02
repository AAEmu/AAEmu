using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: i64 zi, u8 idx, bool enabled. The 1.2 skill id between the
/// streak index and the flag is not read, and sending it pushed "enabled" four bytes out of place.
/// </remarks>
public class SCInstantGameKillstreakPacket(ZoneInstanceId zoneInstanceId, sbyte killstreak, bool enabled)
    : GamePacket(SCOffsets.SCInstantGameKillstreakPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write((byte)killstreak);
        stream.Write(enabled);
        return stream;
    }
}