using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRefreshBotCheckInfoPacket : GamePacket
    {
        public CSRefreshBotCheckInfoPacket() : base(CSOffsets.CSRefreshBotCheckInfoPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRefreshBotCheckInfoPacket");
        }
    }
}
