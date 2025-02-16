using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSearchListPacket : GamePacket
{
    public CSSearchListPacket() : base(CSOffsets.CSSearchListPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var zoneType = stream.ReadByte();
        var onlineType = stream.ReadByte();

        Logger.Debug("SearchList, ZoneType: {0}, OnlineType: {1}", zoneType, onlineType);

        Connection.SendPacket(new SCSearchListPacket(0, [], false));

    }
}
