using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTakedownItemPacket : GamePacket
    {
        public CSTakedownItemPacket() : base(CSOffsets.CSTakedownItemPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSTakedownItemPacket");
        }
    }
}
