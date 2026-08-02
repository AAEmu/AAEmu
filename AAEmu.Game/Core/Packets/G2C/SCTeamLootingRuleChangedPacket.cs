using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamLootingRuleChangedPacket(int teamId, LootingRule lootingRule, LootingRuleChangeFlags changeFlags) : GamePacket(SCOffsets.SCTeamLootingRuleChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // i32 teamId, LootingRule rule, i8 changeFlags.
        stream.Write(teamId);
        stream.Write(lootingRule);
        stream.Write((sbyte)changeFlags);
        return stream;
    }
}
