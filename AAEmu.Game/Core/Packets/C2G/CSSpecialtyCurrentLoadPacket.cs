using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSpecialtyCurrentLoadPacket : GamePacket
    {
        public CSSpecialtyCurrentLoadPacket() : base(CSOffsets.CSSpecialtyCurrentLoadPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSSpecialtyCurrentLoadPacket");
        }
    }
}
