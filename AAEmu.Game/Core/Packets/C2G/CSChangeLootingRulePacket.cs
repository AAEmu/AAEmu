using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeLootingRulePacket : GamePacket
    {
        public CSChangeLootingRulePacket() : base(CSOffsets.CSChangeLootingRulePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();

            var lootingRule = new LootingRule();
            lootingRule.Read(stream);

            var changeFlags = stream.ReadByte();

            // _log.Warn("ChangeLootingRule, TeamId: {0}, Flag: {1}, Rule: {2}/{3}/{4}/{5}", teamId, changeFlags, lootingRule.LootMethod, lootingRule.Type,
            //     lootingRule.Id, lootingRule.RollForBop);
            TeamManager.Instance.ChangeLootingRule(Connection.ActiveChar, teamId, lootingRule, changeFlags);
        }
    }
}
