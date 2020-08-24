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
            var houseId = stream.ReadUInt16(); // tl
            var designId = stream.ReadUInt32(); // templateId
            
            var x = stream.ReadSingle();       // Vector3 posX
            var y = stream.ReadSingle();       // Vector3 posY
            var z = stream.ReadSingle();       // Vector3 posZ

            var rotX = stream.ReadSingle();     // Quaternion RotX
            var rotY = stream.ReadSingle();     // Quaternion RotY
            var rotZ = stream.ReadSingle();     // Quaternion RotZ
            var rotW = stream.ReadSingle();     // Quaternion Scalar

            var objId = stream.ReadBc();
            var itemId = stream.ReadUInt64();

            _log.Debug("DecorateHouse, houseId: {0}, designId: {1}", houseId, designId);
        }
    }
}
