using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSExpeditionChangeMemberRolePacket : GamePacket
{
    public CSExpeditionChangeMemberRolePacket() : base(CSOffsets.CSExpeditionChangeMemberRolePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var role = stream.ReadByte();
        var id = stream.ReadUInt32(); // type(id)

        Logger.Debug($"ExpeditionChangeMemberRole, Id: {id}, Role: {role}");
        ExpeditionManager.ChangeMemberRole(Connection, role, id);
    }
}
