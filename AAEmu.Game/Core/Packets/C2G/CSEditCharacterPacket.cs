using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEditCharacterPacket : GamePacket
    {
        public CSEditCharacterPacket() : base(CSOffsets.CSEditCharacterPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // TODO ... create
            _log.Error("CSEditCharacterPacket is not implemented!");
        }
    }
}
