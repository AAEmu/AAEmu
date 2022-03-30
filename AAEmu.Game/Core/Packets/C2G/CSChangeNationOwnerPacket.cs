using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeNationOwnerPacket : GamePacket
    {
        public CSChangeNationOwnerPacket() : base(CSOffsets.CSChangeNationOwnerPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSChangeNationOwnerPacket");
        }
    }
}
