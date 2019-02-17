using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeExpeditionOwnerPacket : GamePacket
    {
        public CSChangeExpeditionOwnerPacket() : base(0x008, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // probably new owner
            var id = stream.ReadUInt32(); // type(id)

            _log.Debug("ChangeExpeditionOwner, Id: {0}", id);
        }
    }
}
