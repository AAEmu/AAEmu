using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRepresentCharacterPacket : GamePacket
    {
        public CSRepresentCharacterPacket() : base(CSOffsets.CSRepresentCharacterPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRepresentCharacterPacket");
        }
    }
}
