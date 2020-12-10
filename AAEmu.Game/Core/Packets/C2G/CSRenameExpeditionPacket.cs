using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRenameExpeditionPacket : GamePacket
    {
        public CSRenameExpeditionPacket() : base(CSOffsets.CSRenameExpeditionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32(); // type(id)
            var name = stream.ReadString();
            var isExpedition = stream.ReadBoolean();
            
            _log.Debug("RenameExpedition, Id: {0}, Name: {1}, IsExpedition: {2}", id, name, isExpedition);
        }
    }
}
