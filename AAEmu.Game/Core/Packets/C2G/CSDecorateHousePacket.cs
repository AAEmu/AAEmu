using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDecorateHousePacket : GamePacket
    {
        public CSDecorateHousePacket() : base(CSOffsets.CSDecorateHousePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var houseId = stream.ReadUInt16();
            var designId = stream.ReadUInt32();
            var x = stream.ReadSingle();
            var y = stream.ReadSingle();
            var z = stream.ReadSingle();
            // var rot = stream.ReadSingle();
            // var ori = stream.ReadBytes(16);
            var quatX = stream.ReadSingle();
            var quatY = stream.ReadSingle();
            var quatZ = stream.ReadSingle();
            var quatW = stream.ReadSingle();
            
            var parentObjId = stream.ReadBc();
            var itemId = stream.ReadUInt64();

            // X, Y, Z are all relative to the house
            var posVec = new Vector3(x, y, z);
            var quat = new Quaternion(quatX, quatY, quatZ, quatW);
            
            _log.Debug("DecorateHouse, houseId: {0}, designId: {1}, x: {2}, y: {3}, z: {4}, rot {5}, objId: {6}, itemId: {7}", 
                houseId, designId, x, y, z, quat, parentObjId, itemId);

            if (!HousingManager.Instance.DecorateHouse(Connection.ActiveChar, houseId, designId, posVec, quat, parentObjId, itemId))
            {
                Connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotDecorate);
                _log.Warn("DecorateHouse, FAILED with houseId: {0}, designId: {1}, x: {2}, y: {3}, z: {4}, rot {5}, objId: {6}, itemId: {7}", houseId, designId, x, y, z, quat, parentObjId, itemId);                
            }            
        }
    }
}
