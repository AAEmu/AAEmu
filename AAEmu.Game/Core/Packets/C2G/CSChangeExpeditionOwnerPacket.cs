using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeExpeditionOwnerPacket : GamePacket
    {
        public CSChangeExpeditionOwnerPacket() : base(CSOffsets.CSChangeExpeditionOwnerPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32(); // type(id)

            _log.Debug("ChangeExpeditionOwner, Id: {0}", id);
            ExpeditionManager.Instance.ChangeOwner(Connection, id);
        }
    }
}
