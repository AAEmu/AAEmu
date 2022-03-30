using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilySetNamePacket : GamePacket
    {
        public CSFamilySetNamePacket() : base(CSOffsets.CSFamilySetNamePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSFamilySetNamePacket");
        }
    }
}
