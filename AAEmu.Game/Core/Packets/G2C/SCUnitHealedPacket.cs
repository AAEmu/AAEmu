using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitHealedPacket : GamePacket
    {
        private readonly CastAction _castAction;
        private readonly SkillCaster _skillCaster;
        private readonly uint _targetId;
        private readonly byte _healType;
        private readonly byte _skillHitType;
        private readonly int _value;

        public SCUnitHealedPacket(CastAction castAction, SkillCaster skillCaster, uint targetId, HealType healType, SkillHitType skillHitType, int value)
            : base(SCOffsets.SCUnitHealedPacket, 5)
        {
            _castAction = castAction;
            _skillCaster = skillCaster;
            _targetId = targetId;
            _healType = (byte)healType; // 0 = Health, 1 = Mana
            _skillHitType = (byte)skillHitType; // 11 = CriticalHealHit, 13 = HealHit, enum missing?
            _value = value;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_castAction);
            stream.Write(_skillCaster);
            stream.WriteBc(_targetId);
            stream.Write(_healType);     // h
            stream.Write(_skillHitType); // h
            stream.Write(_value);        // a
            stream.Write(0);             // o
            stream.Write((byte)1);       // result -> to debug into
            return stream;
        }
    }
}
