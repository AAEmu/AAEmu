using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeMateNamePacket() : GamePacket(CSOffsets.CSChangeMateNamePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var tlId = stream.ReadUInt16();
        var name = stream.ReadString();

        //Logger.Warn("ChangeMateName, TlId: {0}, Name: {1}", tlId, name);
        Connection.ActiveChar.ParentWorld.MateManager.RenameMount(Connection, tlId, name);
    }
}
