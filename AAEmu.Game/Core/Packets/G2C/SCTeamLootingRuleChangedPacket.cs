using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeamLootingRuleChangedPacket(uint teamId, LootingRule lootingRule, byte changeFlags) : GamePacket(SCOffsets.SCTeamLootingRuleChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(teamId);
        stream.Write(lootingRule);
        stream.Write(changeFlags);
        return stream;
    }
}
