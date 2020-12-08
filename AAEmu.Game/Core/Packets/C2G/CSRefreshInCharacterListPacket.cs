using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRefreshInCharacterListPacket : GamePacket
    {
        public CSRefreshInCharacterListPacket() : base(CSOffsets.CSRefreshInCharacterListPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("RefreshInCharacterList");
            Connection.SendPacket(new SCRefreshInCharacterListPacket());
        }
    }
}
