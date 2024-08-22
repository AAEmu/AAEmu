using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCIndunZone : GamePacket
{
    private readonly Portal[] _portals;

    public SCIndunZone(Portal[] portals) : base(SCOffsets.SCIndunZone, 5)
    {
        _portals = portals;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_portals.Length);

        foreach (var portal in _portals)
            portal.WriteIndunZone(stream);

        return stream;
    }

}
