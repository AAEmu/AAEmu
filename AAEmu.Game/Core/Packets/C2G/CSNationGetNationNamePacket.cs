using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNationGetNationNamePacket : GamePacket
    {
        public CSNationGetNationNamePacket() : base(CSOffsets.CSNationGetNationNamePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Logger.Debug("CSNationGetNationNamePacket");
        }
    }
}
