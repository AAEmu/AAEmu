using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPickBuffInstantGamePacket : GamePacket
    {
        public CSPickBuffInstantGamePacket() : base(CSOffsets.CSPickBuffInstantGamePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSPickBuffInstantGamePacket");
        }
    }
}
