using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpawnSlavePacket : GamePacket
{
    public CSSpawnSlavePacket() : base(CSOffsets.CSSpawnSlavePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var slaveId = stream.ReadUInt32(); // slave
        var x = Helpers.ConvertLongX(stream.ReadInt64()); // WorldPosXYZ_0940
        var y = Helpers.ConvertLongY(stream.ReadInt64());
        var z = stream.ReadSingle();
        var zRot = stream.ReadSingle();   // zrot
        var itemId = stream.ReadUInt64(); // item
        var slotType = (SlotType)stream.ReadByte(); // type
        var slot = stream.ReadByte();          // index
        var hideSpawnEffect = stream.ReadBoolean(); // hideSpawnEffect

        Logger.Debug("SpawnSlave, SlaveId: {0}", slaveId);
    }
}
