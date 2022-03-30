using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDecorateHousePacket : GamePacket
    {
        public CSDecorateHousePacket() : base(CSOffsets.CSDecorateHousePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var houseId = stream.ReadUInt16();  // tl
            var designId = stream.ReadUInt32(); // type
            var x = stream.ReadSingle();        // pos
            var y = stream.ReadSingle();
            var z = stream.ReadSingle();
            var rot = stream.ReadSingle();      // rot  не уверен
            var objId = stream.ReadBc();        // bc
            var itemId = stream.ReadUInt64();   // item

            _log.Debug("DecorateHouse, houseId: {0}, designId: {1}, x: {2}, y: {3}, z: {4}, rot: {5}, objId: {6}, itemId: {7}",
                houseId, designId, x, y, z, rot, objId, itemId);
        }
    }
}
