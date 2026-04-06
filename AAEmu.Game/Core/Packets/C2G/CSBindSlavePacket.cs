using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBindSlavePacket() : GamePacket(CSOffsets.CSBindSlavePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var tlId = stream.ReadUInt16();

        Logger.Debug("BindSlave, Tl: {0}", tlId);
        var character = Connection.ActiveChar;
        if (character?.ParentWorld == null)
            return;

        character.ParentWorld.SlaveManager.BindSlave(Connection, tlId);
    }
}
