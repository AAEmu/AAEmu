using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSExpeditionChangeRolePolicyPacket : GamePacket
{
    public CSExpeditionChangeRolePolicyPacket() : base(CSOffsets.CSExpeditionChangeRolePolicyPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var rolePolicy = new ExpeditionRolePolicy();
        rolePolicy.Read(stream);

        Logger.Debug($"ExpeditionChangeRolePolicy, Id: {rolePolicy.Id}, Role: {rolePolicy.Role}");
        ExpeditionManager.Instance.ChangeExpeditionRolePolicy(Connection, rolePolicy);
    }
}
