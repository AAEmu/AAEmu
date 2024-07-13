using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSExpeditionInvitePacket : GamePacket
{
    public CSExpeditionInvitePacket() : base(CSOffsets.CSExpeditionInvitePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var name = stream.ReadString();

        Logger.Debug($"ExpeditionInvite, Name: {name}");
        ExpeditionManager.Invite(Connection, name);
    }
}
