using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRenameCharacterPacket : GamePacket
    {
        public CSRenameCharacterPacket() : base(CSOffsets.CSRenameCharacterPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRenameCharacterPacket");
        }
    }
}
