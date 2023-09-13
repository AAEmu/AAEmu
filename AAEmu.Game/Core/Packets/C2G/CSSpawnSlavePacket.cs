using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSpawnSlavePacket : GamePacket
    {
        public CSSpawnSlavePacket() : base(CSOffsets.CSSpawnSlavePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var slaveId = stream.ReadUInt32();
            var x = Helpers.ConvertLongX(stream.ReadInt64());
            var y = Helpers.ConvertLongY(stream.ReadInt64());
            var z = stream.ReadSingle();
            var zRot = stream.ReadSingle();
            var itemId = stream.ReadUInt64();

            // TODO : check this part with nikes
            stream.ReadByte();
            var slotType = (SlotType)stream.ReadByte();
            stream.ReadByte();
            var slot = stream.ReadByte();

            var hideSpawnEffect = stream.ReadBoolean();

            _log.Debug("SpawnSlave, SlaveId: {0}", slaveId);
        }
    }
}
