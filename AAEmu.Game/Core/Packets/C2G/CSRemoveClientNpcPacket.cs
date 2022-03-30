using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRemoveClientNpcPacket : GamePacket
    {
        public CSRemoveClientNpcPacket() : base(CSOffsets.CSRemoveClientNpcPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRemoveClientNpcPacket");
        }
    }
}
