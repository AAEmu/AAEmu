using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUnhangPacket : GamePacket
    {
        public CSUnhangPacket() : base(CSOffsets.CSUnhangPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var reason = stream.ReadInt32();
            
            _log.Warn("Unhang, ObjId: {0}, Reason: {1}", objId, reason);
        }
    }
}
