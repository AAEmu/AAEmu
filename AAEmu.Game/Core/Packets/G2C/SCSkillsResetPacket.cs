using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSkillsResetPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly AbilityType _ability;

        public SCSkillsResetPacket(uint objId, AbilityType ability) : base(SCOffsets.SCSkillsResetPacket, 5)
        {
            _objId = objId;
            _ability = ability;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write((byte) _ability);
            return stream;
        }
    }
}
