using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetLogicDoodadPacket : GamePacket
    {
        public CSSetLogicDoodadPacket() : base(CSOffsets.CSSetLogicDoodadPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var obj2Id = stream.ReadBc();

            _log.Warn("SetLogicDoodad, ObjId: {0}, {1}", objId, obj2Id);
        }
    }
}
