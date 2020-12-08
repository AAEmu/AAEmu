using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeDoodadDataPacket : GamePacket
    {
        public CSChangeDoodadDataPacket() : base(CSOffsets.CSChangeDoodadDataPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var data = stream.ReadInt32();
            
            _log.Warn("ChangeDoodadData, ObjId: {0}, Data: {1}", objId, data);
        }
    }
}
