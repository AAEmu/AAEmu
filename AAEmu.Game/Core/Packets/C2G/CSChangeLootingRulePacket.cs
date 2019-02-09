using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeLootingRulePacket : GamePacket
    {
        public CSChangeLootingRulePacket() : base(0x082, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();

            var lootMethod = stream.ReadByte();
            var type = stream.ReadByte();
            var unkId = stream.ReadUInt32(); // type(id)
            var rollForBop = stream.ReadBoolean();

            var changeFlags = stream.ReadByte();

            _log.Warn("ChangeLootingRule, TeamId: {0}");
        }
    }
}
