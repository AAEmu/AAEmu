using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeSlaveNamePacket : GamePacket
{
    public CSChangeSlaveNamePacket() : base(CSOffsets.CSChangeSlaveNamePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var tl = stream.ReadUInt16();
        var name = stream.ReadString();

        Logger.Debug("ChangeSlaveName, Tl: {0}, Name: {1}", tl, name);

        SlaveManager.Instance.RenameSlave(Connection, tl, name);

    }
}
