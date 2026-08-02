using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Leader changed looting rules
/// </summary>
public class CSChangeLootingRulePacket() : GamePacket(CSOffsets.CSChangeLootingRulePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // i32 teamId, LootingRule rule, i8 changeFlags.
        var teamId = stream.ReadInt32();

        var lootingRule = new LootingRule();
        lootingRule.Read(stream);

        var changeFlags = (LootingRuleChangeFlags)stream.ReadSByte();

        Logger.Warn($"ChangeLootingRule, TeamId: {teamId}, Flag: {changeFlags}, Rule Method:{lootingRule.LootMethod}, Grade:{lootingRule.MinimumGrade}, LootMaster: {lootingRule.LootMaster}, RollForBoP: {lootingRule.RollForBindOnPickup}");
        TeamManager.Instance.ChangeLootingRule(Connection.ActiveChar, teamId, changeFlags, lootingRule.LootMethod, lootingRule.MinimumGrade, lootingRule.LootMaster, lootingRule.RollForBindOnPickup);
    }
}
