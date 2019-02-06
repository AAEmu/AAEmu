using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSKickFromExpeditionPacket : GamePacket
    {
        public CSKickFromExpeditionPacket() : base(0x00f, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32(); // type(id)

            _log.Debug("KickFromExpedition, Id: {0}", id);
        }
    }
}
