using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSHangPacket : GamePacket
    {
        public CSHangPacket() : base(CSOffsets.CSHangPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var obj2Id = stream.ReadBc();
            
            _log.Warn("Hang, ObjId: {0}, Obj2Id: {1}", objId, obj2Id);
        }
    }
}
