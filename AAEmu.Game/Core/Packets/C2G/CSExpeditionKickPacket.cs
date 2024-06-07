using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSExpeditionKickPacket : GamePacket
{
    public CSExpeditionKickPacket() : base(CSOffsets.CSExpeditionKickPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var id = stream.ReadUInt32(); // type(id)

        Logger.Debug($"ExpeditionKick, Id: {id}");
        ExpeditionManager.Kick(Connection, id);
    }
}
