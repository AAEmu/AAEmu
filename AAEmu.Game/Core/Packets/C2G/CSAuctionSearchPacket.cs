using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAuctionSearchPacket : GamePacket
    {
        public CSAuctionSearchPacket() : base(0x0b8, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();

            var name = stream.ReadString();
            var exactMatch = stream.ReadBoolean();
            var type = stream.ReadByte();
            var categoryA = stream.ReadByte();
            var categoryB = stream.ReadByte();
            var categoryC = stream.ReadByte();
            var page = stream.ReadInt32();
            var unkId = stream.ReadUInt32(); // type(id)
            var filter = stream.ReadInt32();
            var worldId = stream.ReadByte();
            var minItemLevel = stream.ReadSByte();
            var maxItemLevel = stream.ReadSByte();
            var sortKind = stream.ReadByte();
            var sortOrder = stream.ReadByte();

            _log.Warn("AuctionSearch, NpcObjId: {0}, Name: {1}", npcObjId, name);
        }
    }
}
