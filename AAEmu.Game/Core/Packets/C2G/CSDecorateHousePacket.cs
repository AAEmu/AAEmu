using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDecorateHousePacket : GamePacket
    {
        public CSDecorateHousePacket() : base(0x058, 1) //TODO 1.0 opcode: 0x056
        {
        }

        public override void Read(PacketStream stream)
        {
            var houseId = stream.ReadUInt16();
            var designId = stream.ReadUInt32();
            var x = stream.ReadSingle();
            var y = stream.ReadSingle();
            var z = stream.ReadSingle();
            var rot = stream.ReadSingle();
            var objId = stream.ReadBc();
            var itemId = stream.ReadUInt64();

            _log.Debug("DecorateHouse, houseId: {0}, designId: {1}", houseId, designId);
        }
    }
}
