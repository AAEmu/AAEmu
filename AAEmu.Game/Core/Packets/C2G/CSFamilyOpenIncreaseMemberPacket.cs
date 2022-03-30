using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyOpenIncreaseMemberPacket : GamePacket
    {
        public CSFamilyOpenIncreaseMemberPacket() : base(CSOffsets.CSFamilyOpenIncreaseMemberPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSFamilyOpenIncreaseMemberPacket");
        }
    }
}
