using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCServerFileTimeSyncPacket : GamePacket
{
    private readonly int _timeZoneBais;
    private readonly DateTime _worldFileTime;

    public SCServerFileTimeSyncPacket() : base(SCOffsets.SCServerFileTimeSyncPacket, 5)
    {
        _worldFileTime = DateTime.UtcNow;
        _timeZoneBais = -180;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_worldFileTime);
        stream.Write(_timeZoneBais);
        return stream;
    }
}
