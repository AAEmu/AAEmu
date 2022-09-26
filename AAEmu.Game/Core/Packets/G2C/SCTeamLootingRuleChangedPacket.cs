using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTeamLootingRuleChangedPacket : GamePacket
    {
        private readonly uint _teamId;
        private readonly LootingRule _lootingRule;
        private readonly byte _changeFlags;

        public SCTeamLootingRuleChangedPacket(uint teamId, LootingRule lootingRule, byte changeFlags) : base(SCOffsets.SCTeamLootingRuleChangedPacket, 5)
        {
            _teamId = teamId;
            _lootingRule = lootingRule;
            _changeFlags = changeFlags;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_teamId);
            stream.Write(_lootingRule);
            stream.Write(_changeFlags);
            return stream;
        }
    }
}
