using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSpawnSlavePacket : GamePacket
    {
        public CSSpawnSlavePacket() : base(CSOffsets.CSSpawnSlavePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var slaveId = stream.ReadUInt32();          // slave
            var x = Helpers.ConvertLongX(stream.ReadInt64());
            var y = Helpers.ConvertLongY(stream.ReadInt64());
            var z = stream.ReadSingle();
            var zRot = stream.ReadSingle();            // zRot
            var itemId = stream.ReadUInt64();          // item
            var slotType = (SlotType)stream.ReadByte();     // type
            var slot = stream.ReadByte();               // index
            var hideSpawnEffect = stream.ReadBoolean(); // hideSpawnEffect

            _log.Debug($"SpawnSlave, SlaveId: {slaveId}, x: {x}, y: {y}, z: {z}, zRot: {zRot}, itemId: {itemId}, slotType: {slotType}, slot: {slot}, hideSpawnEffect: {hideSpawnEffect}");
        }
    }
}
