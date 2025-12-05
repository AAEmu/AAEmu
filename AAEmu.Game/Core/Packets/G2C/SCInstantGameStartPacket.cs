using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstantGameStartPacket : GamePacket
{
    private ZoneInstanceId _zoneInstanceId;
    private uint _start;
    private uint _now;

    public SCInstantGameStartPacket(ZoneInstanceId zoneInstanceId, uint start, uint now)
        : base(SCOffsets.SCInstantGameStartPacket, 1)
    {
        _zoneInstanceId = zoneInstanceId;
        _start = start;
        _now = now;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_zoneInstanceId);
        stream.Write(_now);
        stream.Write(_start);
        return stream;
    }
}