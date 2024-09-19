using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBindSlavePacket : GamePacket
{
    public CSBindSlavePacket() : base(CSOffsets.CSBindSlavePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var tlId = stream.ReadUInt16(); // target

        Logger.Debug("BindSlave, Tl: {0}", tlId);
        SlaveManager.Instance.BindSlave(Connection, tlId);
    }
}
