using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCListSkillActiveTypsPacket : GamePacket
    {
        private readonly (uint _skillId, AbilityType _ability)[] _skillActiveTyps;

        public SCListSkillActiveTypsPacket((uint skillId, AbilityType ability)[] skillActiveTyps) : base(SCOffsets.SCListSkillActiveTypsPacket, 5)
        {
            _skillActiveTyps = skillActiveTyps;
        }

        public override PacketStream Write(PacketStream stream)
        {
            var count = _skillActiveTyps.Length;
            stream.Write(count);
            for (var i = 0; i < count; i++) // max 100
            {
                stream.Write(_skillActiveTyps[i]._skillId);       // skillType (type)
                stream.Write((byte)_skillActiveTyps[i]._ability); // activeType
            }
            return stream;
        }
    }
}
