using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilySetContentPacket : GamePacket
    {
        public CSFamilySetContentPacket() : base(CSOffsets.CSFamilySetContentPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSFamilySetContentPacket");
        }
    }
}
