using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCListSkillActiveTypsPacket : GamePacket
    {
        private readonly (uint _skillId, AbilityType _ability)[] _skillActiveTyps;

        public SCListSkillActiveTypsPacket((uint skillId, AbilityType ability)[] skillActiveTyps)
            : base(SCOffsets.SCListSkillActiveTypsPacket, 5)
        {
            _skillActiveTyps = skillActiveTyps;
        }

        public override PacketStream Write(PacketStream stream)
        {
            //TODO заготовка для пакета

            var count = _skillActiveTyps.Length;
            stream.Write(count); // 200 max
            for (var i = 0; i < count; i++)
            {
                stream.Write(0u);       // heirSkillType (UInt32)
                stream.Write(_skillActiveTyps[i]._skillId);       // skillType (UInt32)
                stream.Write((byte)_skillActiveTyps[i]._ability); // activeTipe
            }
            return stream;
        }
    }
}
