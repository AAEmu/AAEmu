using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCreateExpeditionPacket : GamePacket
    {
        public CSCreateExpeditionPacket() : base(CSOffsets.CSCreateExpeditionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();
            var id = stream.ReadUInt32(); // TODO character id?
            
            _log.Debug("CreateExpedition, name: {0}, id: {1}", name, id);
            ExpeditionManager.Instance.CreateExpedition(name, Connection);
        }
    }
}
