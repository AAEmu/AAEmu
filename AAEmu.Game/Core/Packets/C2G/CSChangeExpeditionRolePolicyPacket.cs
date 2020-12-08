using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeExpeditionRolePolicyPacket : GamePacket
    {
        public CSChangeExpeditionRolePolicyPacket() : base(CSOffsets.CSChangeExpeditionRolePolicyPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var rolePolicy = new ExpeditionRolePolicy();
            rolePolicy.Read(stream);

            _log.Debug("ChangeExpeditionRolePolicy, Id: {0}, Role: {1}", rolePolicy.Id, rolePolicy.Role);
            ExpeditionManager.Instance.ChangeExpeditionRolePolicy(Connection, rolePolicy);
        }
    }
}
